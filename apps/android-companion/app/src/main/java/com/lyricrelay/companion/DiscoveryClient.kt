package com.lyricrelay.companion

import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

object DiscoveryClient {
    private const val PORT = 47251
    private const val REQUEST = "LYRICRELAY_DISCOVER"

    fun discover(windowsDeviceId: String, pinnedFingerprint: String): Triple<String, Int, String>? {
        return runCatching {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.soTimeout = 500
                val request = REQUEST.toByteArray()
                socket.send(DatagramPacket(request, request.size, InetAddress.getByName("255.255.255.255"), PORT))
                val buffer = ByteArray(2048)
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)
                val response = JSONObject(String(packet.data, 0, packet.length))
                if (response.optString("deviceId") != windowsDeviceId) return null
                val fingerprint = response.getString("certificateSha256")
                if (!fingerprint.equals(pinnedFingerprint, ignoreCase = true)) return null
                Triple(packet.address.hostAddress ?: return null, response.getInt("port"), fingerprint)
            }
        }.getOrNull()
    }
}
