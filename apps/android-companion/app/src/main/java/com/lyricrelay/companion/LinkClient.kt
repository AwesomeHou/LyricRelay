package com.lyricrelay.companion

import android.util.Base64
import org.json.JSONObject
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.IOException
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.InetSocketAddress
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

class LinkClient(private var config: PairingConfig, private val deviceId: String, private val store: PairingStore) : AutoCloseable {
    private var socket: SSLSocket? = null
    private var writer: BufferedWriter? = null
    private var reader: BufferedReader? = null

    @Synchronized
    fun connect() {
        ensureConnected()
    }

    @Synchronized
    fun send(state: TrackState) {
        ensureConnected()
        write(state.toEnvelope(deviceId))
    }

    @Synchronized
    fun sendCleared() {
        ensureConnected()
        write(
            JSONObject()
                .put("version", 1)
                .put("type", "track.cleared")
                .put("messageId", java.util.UUID.randomUUID().toString())
                .put("deviceId", deviceId)
                .put("sentAt", java.time.Instant.now().toString())
                .put("payload", JSONObject())
        )
    }

    @Synchronized
    fun sendPing() {
        ensureConnected()
        write(
            JSONObject()
                .put("version", 1)
                .put("type", "link.ping")
                .put("messageId", java.util.UUID.randomUUID().toString())
                .put("deviceId", deviceId)
                .put("sentAt", java.time.Instant.now().toString())
                .put("payload", JSONObject())
        )
        val responseLine = reader?.readLine() ?: throw IOException("Windows closed the connection")
        val response = JSONObject(responseLine)
        if (response.optString("type") != "link.pong") {
            throw IOException("Unexpected link response")
        }
    }

    @Synchronized
    private fun ensureConnected() {
        if (socket?.isConnected == true && socket?.isClosed == false) return
        close()
        val context = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf(PinnedTrustManager(config.certificateSha256)), null)
        }
        val newSocket = context.socketFactory.createSocket() as SSLSocket
        newSocket.connect(InetSocketAddress(config.host, config.port), 5000)
        newSocket.soTimeout = 5000
        newSocket.startHandshake()
        socket = newSocket
        writer = BufferedWriter(OutputStreamWriter(newSocket.outputStream, Charsets.UTF_8))
        reader = BufferedReader(InputStreamReader(newSocket.inputStream, Charsets.UTF_8))
        val type = if (config.deviceKey == null) "pairing.confirm" else "link.hello"
        val payload = JSONObject().put("androidDeviceId", deviceId)
        if (config.deviceKey == null) {
            payload.put("token", config.token)
        } else {
            payload.put("windowsDeviceId", config.windowsDeviceId)
            payload.put("deviceKey", config.deviceKey)
        }
        val hello = JSONObject()
            .put("version", 1)
            .put("type", type)
            .put("messageId", java.util.UUID.randomUUID().toString())
            .put("deviceId", deviceId)
            .put("sentAt", java.time.Instant.now().toString())
            .put("payload", payload)
        writer!!.write(hello.toString())
        writer!!.newLine()
        writer!!.flush()
        val response = JSONObject(reader!!.readLine() ?: error("Windows closed the connection"))
        if (response.optString("type") == "pairing.accept") {
            val returnedKey = response.optJSONObject("payload")?.optString("deviceKey").orEmpty()
            if (returnedKey.isNotBlank()) {
                config = config.copy(deviceKey = returnedKey)
                store.save(config)
            }
        } else if (response.optString("type") != "link.hello") {
            error("Windows rejected the connection")
        }
    }

    private fun write(message: JSONObject) {
        writer!!.write(message.toString())
        writer!!.newLine()
        writer!!.flush()
    }

    @Synchronized
    override fun close() {
        runCatching { writer?.close() }
        runCatching { reader?.close() }
        runCatching { socket?.close() }
        writer = null
        reader = null
        socket = null
    }
}

private class PinnedTrustManager(private val expected: String) : X509TrustManager {
    override fun checkClientTrusted(chain: Array<out X509Certificate>, authType: String) = Unit

    override fun checkServerTrusted(chain: Array<out X509Certificate>, authType: String) {
        if (chain.isEmpty()) throw CertificateException("empty certificate chain")
        val actual = MessageDigest.getInstance("SHA-256").digest(chain[0].encoded)
            .joinToString("") { "%02x".format(it) }
        if (!actual.equals(expected.replace(":", "").lowercase(), ignoreCase = true)) {
            throw CertificateException("certificate fingerprint mismatch")
        }
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}
