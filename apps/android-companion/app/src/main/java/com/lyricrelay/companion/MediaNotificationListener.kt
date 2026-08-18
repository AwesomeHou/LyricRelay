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
        override fun onPlaybackStateChanged(state: PlaybackState?) = publishCurrentThrottled()
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
        unregisterReceiver(refreshReceiver)
        super.onDestroy()
    }

    override fun onListenerConnected() {
        publishCurrent()
    }

    override fun onNotificationPosted(sbn: android.service.notification.StatusBarNotification) {
        publishCurrentThrottled()
    }

    private fun publishCurrentThrottled() {
        val now = SystemClock.elapsedRealtime()
        if (now - lastPublishedAtElapsedMs < 500L) return
        lastPublishedAtElapsedMs = now
        publishCurrent()
    }

    private fun publishCurrent() {
        val controllers = runCatching {
            sessionManager.getActiveSessions(ComponentName(this, MediaNotificationListener::class.java))
        }.getOrDefault(emptyList())

        controllers.forEach { controller ->
            runCatching { controller.unregisterCallback(callback) }
            controller.registerCallback(callback)
        }

        val controller = selectController(controllers)
        val state = controller?.let { toTrackState(it) }
        val intent = Intent(ACTION_MEDIA_STATE).setPackage(packageName)
        if (state == null) {
            intent.putExtra(EXTRA_STATE, JSONObjectState.cleared())
        } else {
            intent.putExtra(EXTRA_STATE, state.toJson().toString())
        }
        sendBroadcast(intent)
    }

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
        val title = metadata.getString(MediaMetadata.METADATA_KEY_TITLE)?.trim().orEmpty()
        if (title.isEmpty()) return null
        val artist = metadata.getString(MediaMetadata.METADATA_KEY_ARTIST)?.trim()
        val album = metadata.getString(MediaMetadata.METADATA_KEY_ALBUM)?.trim()
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
        val mediaId = metadata.getString(MediaMetadata.METADATA_KEY_MEDIA_ID)?.takeIf { it.isNotBlank() }
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

    companion object {
        const val ACTION_MEDIA_STATE = "com.lyricrelay.companion.MEDIA_STATE"
        const val ACTION_MEDIA_REFRESH = "com.lyricrelay.companion.MEDIA_REFRESH"
        const val EXTRA_STATE = "state"
        private const val PERIODIC_REFRESH_MS = 2000L
    }
}

private object JSONObjectState {
    fun cleared(): String = "{\"state\":\"stopped\",\"positionMs\":0,\"playbackSpeed\":1.0,\"stateVersion\":0}"
}
