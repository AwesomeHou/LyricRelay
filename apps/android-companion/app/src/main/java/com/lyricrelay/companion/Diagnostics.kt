package com.lyricrelay.companion

import android.util.Log
import java.security.MessageDigest

internal object RelayDiagnostics {
    private const val TAG = "LyricRelay.Debug"

    fun log(scope: String, message: String) {
        if (BuildConfig.DEBUG) Log.d(TAG, "$scope $message")
    }

    fun stateSummary(state: TrackState): String =
        "track=${hash(state.trackId)} pkg=${state.packageName ?: "-"} " +
            "title=${hash(state.title)} artist=${hash(state.artist)} album=${hash(state.album)} " +
            "duration=${state.durationMs ?: "-"} state=${state.state} position=${state.positionMs} " +
            "speed=${"%.3f".format(state.playbackSpeed)} version=${state.stateVersion}"

    fun hash(value: String?): String {
        if (value.isNullOrEmpty()) return "-"
        val digest = MessageDigest.getInstance("SHA-256").digest(value.toByteArray())
        return digest.take(4).joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }
}
