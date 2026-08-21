package com.lyricrelay.companion

import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.service.notification.NotificationListenerService
import android.content.ComponentName
import android.content.Intent
import android.media.session.PlaybackState
import android.os.Build
import android.content.BroadcastReceiver
import android.content.Context
import android.content.IntentFilter
import java.security.MessageDigest

class MediaNotificationListener : NotificationListenerService() {
    private lateinit var sessionManager: MediaSessionManager
    private var lastPublishedAtElapsedMs = 0L
    private var lastValidStateAtElapsedMs = 0L
    private var clearPublished = false
    private val registeredControllers = mutableMapOf<String, MediaController>()
    private val refreshHandler = Handler(Looper.getMainLooper())
    private val periodicRefresh = object : Runnable {
        override fun run() {
            publishCurrent()
            refreshHandler.postDelayed(this, PERIODIC_REFRESH_MS)
        }
    }
    private val refreshReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action == ACTION_MEDIA_REFRESH) publishCurrent()
        }
    }
    private val callback = object : MediaController.Callback() {
        // A playback callback represents a control-state change or a seek.
        // Deliver it immediately so Windows can rebase its monotonic timeline
        // instead of waiting for the next periodic calibration.
        override fun onPlaybackStateChanged(state: PlaybackState?) = publishCurrent()
        override fun onMetadataChanged(metadata: MediaMetadata?) = publishCurrentThrottled()
    }

    override fun onCreate() {
        super.onCreate()
        sessionManager = getSystemService(MediaSessionManager::class.java)
        val filter = IntentFilter(ACTION_MEDIA_REFRESH)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(refreshReceiver, filter, RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("DEPRECATION")
            registerReceiver(refreshReceiver, filter)
        }
        refreshHandler.post(periodicRefresh)
    }

    override fun onDestroy() {
        refreshHandler.removeCallbacks(periodicRefresh)
        registeredControllers.values.forEach { controller ->
            runCatching { controller.unregisterCallback(callback) }
        }
        registeredControllers.clear()
        unregisterReceiver(refreshReceiver)
        super.onDestroy()
    }

    override fun onListenerConnected() {
        publishCurrent()
    }

    override fun onNotificationPosted(sbn: android.service.notification.StatusBarNotification) {
        // MediaController callbacks and the periodic calibration are enough.
        // Scanning on every notification also reacts to unrelated apps.
    }

    private fun publishCurrentThrottled() {
        val now = SystemClock.elapsedRealtime()
        if (now - lastPublishedAtElapsedMs < 500L) return
        lastPublishedAtElapsedMs = now
        publishCurrent()
    }

    private fun publishCurrent() {
        lastPublishedAtElapsedMs = SystemClock.elapsedRealtime()
        val controllers = runCatching {
            sessionManager.getActiveSessions(ComponentName(this, MediaNotificationListener::class.java))
        }.getOrDefault(emptyList())

        synchronizeCallbacks(controllers)

        val controller = selectController(controllers)
        val state = controller?.let { toTrackState(it) }
        val intent = Intent(ACTION_MEDIA_STATE).setPackage(packageName)
        if (state != null) {
            RelayDiagnostics.log("media", "publish controllers=${controllers.size} ${RelayDiagnostics.stateSummary(state)}")
            lastValidStateAtElapsedMs = SystemClock.elapsedRealtime()
            clearPublished = false
            intent.putExtra(EXTRA_STATE, state.toJson().toString())
        } else if (lastValidStateAtElapsedMs > 0L &&
            SystemClock.elapsedRealtime() - lastValidStateAtElapsedMs < EMPTY_STATE_GRACE_MS) {
            RelayDiagnostics.log("media", "publish skipped reason=empty-session-within-grace controllers=${controllers.size}")
            // MediaSession metadata can be empty briefly while a player
            // refreshes its notification. Keep RelayService's last valid
            // state so Windows can continue extrapolating the timeline.
            return
        } else if (clearPublished) {
            RelayDiagnostics.log("media", "publish skipped reason=already-cleared controllers=${controllers.size}")
            return
        } else {
            clearPublished = true
            RelayDiagnostics.log("media", "publish cleared controllers=${controllers.size}")
            intent.putExtra(EXTRA_STATE, JSONObjectState.cleared())
        }
        sendBroadcast(intent)
    }

    private fun synchronizeCallbacks(controllers: List<MediaController>) {
        val activeKeys = controllers.map(::sessionKey).toSet()
        registeredControllers.keys
            .filter { it !in activeKeys }
            .toList()
            .forEach { key ->
                registeredControllers.remove(key)?.let { controller ->
                    runCatching { controller.unregisterCallback(callback) }
                }
            }

        controllers.forEach { controller ->
            val key = sessionKey(controller)
            if (registeredControllers.containsKey(key)) return@forEach
            runCatching {
                controller.registerCallback(callback)
                registeredControllers[key] = controller
            }
        }
    }

    private fun sessionKey(controller: MediaController): String =
        "${controller.packageName}:${controller.sessionToken}"

    private fun selectController(controllers: List<MediaController>): MediaController? {
        return controllers
            .sortedWith(
                compareByDescending<MediaController> {
                    it.playbackState?.state == PlaybackState.STATE_PLAYING
                }.thenByDescending { it.playbackState?.lastPositionUpdateTime ?: 0L }
            )
            .firstOrNull()
    }

    private fun toTrackState(controller: MediaController): TrackState? {
        val metadata = controller.metadata ?: return null
        val description = metadata.description
        val rawTitle = firstText(
            metadata.getString(MediaMetadata.METADATA_KEY_TITLE),
            metadata.getString(MediaMetadata.METADATA_KEY_DISPLAY_TITLE),
            description?.title,
            description?.subtitle
        ).orEmpty()
        if (rawTitle.isEmpty()) return null
        val rawArtist = firstText(
            metadata.getString(MediaMetadata.METADATA_KEY_ARTIST),
            metadata.getString(MediaMetadata.METADATA_KEY_DISPLAY_SUBTITLE),
            description?.subtitle,
            description?.description
        )
        val normalized = normalizeQqMetadata(controller.packageName, rawTitle, rawArtist)
        val title = normalized.first
        val artist = normalized.second
        val album = firstText(
            metadata.getString(MediaMetadata.METADATA_KEY_ALBUM),
            description?.description
        )
        val duration = metadata.getLong(MediaMetadata.METADATA_KEY_DURATION).takeIf { it > 0 }
        val playback = controller.playbackState
        val state = when (playback?.state) {
            PlaybackState.STATE_PLAYING -> PlaybackStateName.PLAYING
            PlaybackState.STATE_PAUSED -> PlaybackStateName.PAUSED
            else -> PlaybackStateName.STOPPED
        }
        val speed = playback?.playbackSpeed?.toDouble()?.takeIf { it > 0 } ?: 1.0
        val basePosition = playback?.position?.coerceAtLeast(0) ?: 0
        val position = if (state == PlaybackStateName.PLAYING && playback != null && playback.lastPositionUpdateTime > 0) {
            basePosition + ((SystemClock.elapsedRealtime() - playback.lastPositionUpdateTime) * speed).toLong()
        } else {
            basePosition
        }
        RelayDiagnostics.log(
            "media-raw",
            "pkg=${controller.packageName} state=$state rawPosition=$basePosition " +
                "lastUpdateAgeMs=${if ((playback?.lastPositionUpdateTime ?: 0L) > 0L) SystemClock.elapsedRealtime() - playback!!.lastPositionUpdateTime else -1} " +
                "derivedPosition=$position speed=${"%.3f".format(speed)}"
        )
        // QQ Music can reuse or mutate its MediaSession mediaId while the
        // same song is playing. Use stable metadata for its track identity.
        val mediaId = metadata.getString(MediaMetadata.METADATA_KEY_MEDIA_ID)
            ?.takeIf { it.isNotBlank() && controller.packageName != QQ_MUSIC_PACKAGE }
        val trackId = mediaId ?: stableTrackId(controller.packageName, title, artist, album, duration)
        return TrackState(
            trackId = trackId,
            title = title,
            artist = artist,
            album = album,
            durationMs = duration,
            packageName = controller.packageName,
            state = state,
            positionMs = position,
            playbackSpeed = speed,
            stateVersion = SystemClock.elapsedRealtime()
        )
    }

    private fun stableTrackId(packageName: String?, title: String, artist: String?, album: String?, durationMs: Long?): String {
        val input = listOf(packageName, title, artist, album, durationMs?.toString()).joinToString("|")
        val digest = MessageDigest.getInstance("SHA-256").digest(input.toByteArray())
        return digest.joinToString("") { "%02x".format(it) }
    }

    private fun firstText(vararg values: CharSequence?): String? = values
        .asSequence()
        .map { it?.toString()?.trim().orEmpty() }
        .firstOrNull { it.isNotEmpty() }

    private fun normalizeQqMetadata(packageName: String?, title: String, artist: String?): Pair<String, String?> {
        if (packageName != QQ_MUSIC_PACKAGE || artist.isNullOrBlank()) return title to artist

        // QQ Music may expose the changing lyric fragment as TITLE and put
        // the stable song title and artist in one hyphenated ARTIST field.
        val parts = artist.split(Regex("\\s*[-–—]\\s*"), limit = 2)
            .map(String::trim)
            .filter(String::isNotEmpty)
        return if (parts.size == 2) parts[0] to parts[1] else title to artist
    }

    companion object {
        private const val QQ_MUSIC_PACKAGE = "com.tencent.qqmusic"
        const val ACTION_MEDIA_STATE = "com.lyricrelay.companion.MEDIA_STATE"
        const val ACTION_MEDIA_REFRESH = "com.lyricrelay.companion.MEDIA_REFRESH"
        const val EXTRA_STATE = "state"
        private const val PERIODIC_REFRESH_MS = 2000L
        private const val EMPTY_STATE_GRACE_MS = 4000L
    }
}

private object JSONObjectState {
    fun cleared(): String = "{\"state\":\"stopped\",\"positionMs\":0,\"playbackSpeed\":1.0,\"stateVersion\":0}"
}
