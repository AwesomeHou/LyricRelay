package com.lyricrelay.companion

import android.util.Base64
import org.json.JSONObject
import java.time.Instant
import java.util.UUID

enum class PlaybackStateName { PLAYING, PAUSED, STOPPED }

data class TrackState(
    val trackId: String,
    val title: String,
    val artist: String?,
    val album: String?,
    val durationMs: Long?,
    val packageName: String?,
    val state: PlaybackStateName,
    val positionMs: Long,
    val playbackSpeed: Double,
    val stateVersion: Long
) {
    fun toEnvelope(deviceId: String, type: String = "track.state"): JSONObject = JSONObject()
        .put("version", 1)
        .put("type", type)
        .put("messageId", UUID.randomUUID().toString())
        .put("deviceId", deviceId)
        .put("sentAt", Instant.now().toString())
        .put("payload", toJson())

    fun toJson(): JSONObject = JSONObject()
        .put("trackId", trackId)
        .put("title", title)
        .put("artist", artist ?: JSONObject.NULL)
        .put("album", album ?: JSONObject.NULL)
        .put("durationMs", durationMs ?: JSONObject.NULL)
        .put("packageName", packageName ?: JSONObject.NULL)
        .put("state", state.name.lowercase())
        .put("positionMs", positionMs.coerceAtLeast(0))
        .put("playbackSpeed", playbackSpeed.coerceIn(0.1, 8.0))
        .put("stateVersion", stateVersion)

    companion object {
        fun fromJson(payload: JSONObject): TrackState = TrackState(
            trackId = payload.getString("trackId"),
            title = payload.getString("title"),
            artist = payload.optNullableString("artist"),
            album = payload.optNullableString("album"),
            durationMs = payload.optNullableLong("durationMs"),
            packageName = payload.optNullableString("packageName"),
            state = when (payload.optString("state", "stopped")) {
                "playing" -> PlaybackStateName.PLAYING
                "paused" -> PlaybackStateName.PAUSED
                else -> PlaybackStateName.STOPPED
            },
            positionMs = payload.optLong("positionMs", 0).coerceAtLeast(0),
            playbackSpeed = payload.optDouble("playbackSpeed", 1.0).coerceIn(0.1, 8.0),
            stateVersion = payload.optLong("stateVersion", 0).coerceAtLeast(0)
        )
    }
}

data class PairingConfig(
    val host: String,
    val port: Int,
    val token: String,
    val certificateSha256: String,
    val windowsDeviceId: String,
    val expiresAt: String,
    val deviceKey: String? = null
) {
    fun toJson(): JSONObject = JSONObject()
        .put("host", host)
        .put("port", port)
        .put("token", token)
        .put("certificateSha256", certificateSha256)
        .put("windowsDeviceId", windowsDeviceId)
        .put("expiresAt", expiresAt)
        .put("deviceKey", deviceKey ?: JSONObject.NULL)

    companion object {
        fun fromQr(raw: String): PairingConfig {
            val json = String(Base64.decode(raw, Base64.URL_SAFE or Base64.NO_WRAP), Charsets.UTF_8)
            val value = JSONObject(json)
            return PairingConfig(
                host = value.getString("host"),
                port = value.getInt("port"),
                token = value.getString("token"),
                certificateSha256 = value.getString("certificateSha256"),
                windowsDeviceId = value.getString("windowsDeviceId"),
                expiresAt = value.getString("expiresAt")
            )
        }
    }
}

fun JSONObject.optNullableString(name: String): String? =
    if (!has(name) || isNull(name)) null else optString(name).takeIf { it.isNotBlank() }

fun JSONObject.optNullableLong(name: String): Long? =
    if (isNull(name) || !has(name)) null else optLong(name)
