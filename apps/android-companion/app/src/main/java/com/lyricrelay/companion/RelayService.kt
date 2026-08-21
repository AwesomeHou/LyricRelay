package com.lyricrelay.companion

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.IBinder
import android.os.SystemClock
import android.util.Log
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

class RelayService : Service() {
    private lateinit var store: PairingStore
    private lateinit var executor: ScheduledExecutorService
    @Volatile private var latest: TrackState? = null
    @Volatile private var latestAtElapsedMs: Long = 0
    @Volatile private var latestGeneration: Long = 0
    @Volatile private var forcePending = false
    private val sendScheduled = AtomicBoolean(false)
    private var lastSentSignature: String? = null
    private var client: LinkClient? = null

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != MediaNotificationListener.ACTION_MEDIA_STATE) return
            val raw = intent.getStringExtra(MediaNotificationListener.EXTRA_STATE) ?: return
            latest = runCatching { TrackState.fromJson(org.json.JSONObject(raw)) }.getOrNull()
            RelayDiagnostics.log("relay", "received ${latest?.let(RelayDiagnostics::stateSummary) ?: "invalid-state"}")
            latestAtElapsedMs = SystemClock.elapsedRealtime()
            latestGeneration++
            scheduleLatestSend(force = true)
        }
    }

    override fun onCreate() {
        super.onCreate()
        store = PairingStore(this)
        executor = Executors.newSingleThreadScheduledExecutor()
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, notification())
        val filter = IntentFilter(MediaNotificationListener.ACTION_MEDIA_STATE)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(receiver, filter, RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("DEPRECATION")
            registerReceiver(receiver, filter)
        }
        sendBroadcast(Intent(MediaNotificationListener.ACTION_MEDIA_REFRESH).setPackage(packageName))
        executor.scheduleAtFixedRate({ scheduleLatestSend(force = false) }, 2, 2, TimeUnit.SECONDS)
        executor.execute { connectIfConfigured() }
    }

    override fun onDestroy() {
        unregisterReceiver(receiver)
        executor.shutdownNow()
        client?.close()
        super.onDestroy()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // The service may already be alive when the user reopens the app or
        // when Windows reconnects. Ask the MediaSession listener to publish
        // its current session on every explicit service start.
        sendBroadcast(Intent(MediaNotificationListener.ACTION_MEDIA_REFRESH).setPackage(packageName))
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun sendLatest(force: Boolean) {
        runCatching {
            val current = client ?: createClient().also { client = it }
            current.sendPing()
            val source = latest ?: run {
                current.sendCleared()
                lastSentSignature = null
                return@runCatching
            }
            // MediaNotificationListener reports the position at broadcast
            // time. Account only for the relay queue/network wait here;
            // Windows performs the final extrapolation from receive time.
            val elapsed = (SystemClock.elapsedRealtime() - latestAtElapsedMs).coerceAtLeast(0)
            val state = if (source.state == PlaybackStateName.PLAYING) {
                source.copy(positionMs = source.positionMs + (elapsed * source.playbackSpeed).toLong())
            } else {
                source
            }
            RelayDiagnostics.log(
                "relay",
                "send force=$force elapsedMs=$elapsed sourcePosition=${source.positionMs} adjustedPosition=${state.positionMs} ${RelayDiagnostics.stateSummary(state)}"
            )
            val signature = "${state.trackId}:${state.state}:${state.positionMs}:${state.playbackSpeed}:${state.stateVersion}"
            if (!force && state.state != PlaybackStateName.PLAYING && signature == lastSentSignature) return@runCatching
            current.send(state)
            lastSentSignature = signature
        }.onFailure {
            Log.w(TAG, "send failed: ${it.javaClass.simpleName}: ${it.message}")
            client?.close()
            client = null
        }
    }

    private fun scheduleLatestSend(force: Boolean) {
        if (force) forcePending = true
        if (sendScheduled.compareAndSet(false, true)) {
            executor.execute {
                val generation = latestGeneration
                val shouldForce = forcePending
                forcePending = false
                sendLatest(force = shouldForce)
                sendScheduled.set(false)
                if (latestGeneration != generation || forcePending) {
                    scheduleLatestSend(force = false)
                }
            }
        }
    }

    private fun connectIfConfigured() {
        runCatching {
            val current = client ?: createClient().also { client = it }
            current.connect()
        }.onFailure {
            Log.w(TAG, "connect failed: ${it.javaClass.simpleName}: ${it.message}")
            client?.close()
            client = null
        }
    }

    private fun createClient(): LinkClient {
        val savedConfig = store.read() ?: error("pairing is not configured")
        val discovered = DiscoveryClient.discover(savedConfig.windowsDeviceId, savedConfig.certificateSha256)
        val config = discovered?.let { (host, port, fingerprint) ->
            savedConfig.copy(host = host, port = port, certificateSha256 = fingerprint).also(store::save)
        } ?: savedConfig
        return LinkClient(config, store.deviceId(), store)
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            getSystemService(NotificationManager::class.java).createNotificationChannel(
                NotificationChannel(CHANNEL_ID, "LyricRelay connection", NotificationManager.IMPORTANCE_LOW)
            )
        }
    }

    @Suppress("DEPRECATION")
    private fun notification(): Notification = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
        Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("LyricRelay")
            .setContentText("正在同步手机播放状态")
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setOngoing(true)
            .build()
    } else {
        Notification.Builder(this)
            .setContentTitle("LyricRelay")
            .setContentText("正在同步手机播放状态")
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setOngoing(true)
            .build()
    }

    companion object {
        private const val TAG = "LyricRelay"
        private const val CHANNEL_ID = "lyricrelay.connection"
        private const val NOTIFICATION_ID = 1001
    }
}
