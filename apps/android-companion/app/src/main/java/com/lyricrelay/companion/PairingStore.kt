package com.lyricrelay.companion

import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.nio.ByteBuffer
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class PairingStore(context: Context) {
    private val prefs: SharedPreferences = context.getSharedPreferences("pairing", Context.MODE_PRIVATE)
    private val keyAlias = "lyricrelay-pairing-key"

    fun read(): PairingConfig? = prefs.getString("config", null)?.let {
        runCatching { PairingConfig.fromStoredJson(JSONObject(decrypt(it))) }.getOrNull()
    }

    fun save(config: PairingConfig) {
        prefs.edit().putString("config", encrypt(config.toJson().toString())).apply()
    }

    fun deviceId(): String = prefs.getString("deviceId", null) ?: java.util.UUID.randomUUID().toString().also {
        prefs.edit().putString("deviceId", it).apply()
    }

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        val existing = store.getKey(keyAlias, null) as? SecretKey
        if (existing != null) return existing
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(
                KeyGenParameterSpec.Builder(
                    keyAlias,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
                ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .build()
            )
        }.generateKey()
    }

    private fun encrypt(value: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val encrypted = cipher.doFinal(value.toByteArray())
        val payload = ByteBuffer.allocate(4 + cipher.iv.size + encrypted.size)
        payload.putInt(cipher.iv.size).put(cipher.iv).put(encrypted)
        return Base64.encodeToString(payload.array(), Base64.NO_WRAP)
    }

    private fun decrypt(value: String): String {
        val bytes = Base64.decode(value, Base64.NO_WRAP)
        val payload = ByteBuffer.wrap(bytes)
        val iv = ByteArray(payload.int).also(payload::get)
        val encrypted = ByteArray(payload.remaining()).also(payload::get)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, iv))
        return String(cipher.doFinal(encrypted))
    }
}

private fun PairingConfig.Companion.fromStoredJson(value: JSONObject): PairingConfig = PairingConfig(
    host = value.getString("host"),
    port = value.getInt("port"),
    token = value.getString("token"),
    certificateSha256 = value.getString("certificateSha256"),
    windowsDeviceId = value.getString("windowsDeviceId"),
    expiresAt = value.getString("expiresAt"),
    deviceKey = value.optString("deviceKey").takeIf { it.isNotBlank() && it != "null" }
)
