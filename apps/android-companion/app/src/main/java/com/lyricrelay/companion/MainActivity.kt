package com.lyricrelay.companion

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.os.Bundle
import android.provider.Settings
import android.view.Gravity
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

class MainActivity : Activity() {
    private companion object {
        const val NotificationPermissionRequest = 100
        const val CameraPermissionRequest = 101
        const val QrScanRequest = 102
    }

    private lateinit var status: TextView
    private lateinit var pairingStore: PairingStore

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pairingStore = PairingStore(this)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(32, 48, 32, 32)
            gravity = Gravity.CENTER_HORIZONTAL
        }
        root.addView(TextView(this).apply {
            text = "LyricRelay"
            textSize = 28f
            setTextColor(Color.BLACK)
        })
        root.addView(TextView(this).apply {
            text = "读取 Android 当前播放状态，并同步到 Windows 任务栏。不会传输音频。"
            textSize = 16f
            setPadding(0, 24, 0, 24)
        })
        status = TextView(this).apply { textSize = 15f }
        root.addView(status)
        root.addView(Button(this).apply {
            text = "打开媒体访问授权"
            setOnClickListener { startActivity(Intent("android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS")) }
        })
        root.addView(Button(this).apply {
            text = "扫描 Windows 配对二维码"
            setOnClickListener { startQrScanner() }
        })
        setContentView(root)
        if (android.os.Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), NotificationPermissionRequest)
        }
        startRelayService()
        updateStatus()
    }

    override fun onResume() {
        super.onResume()
        updateStatus()
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        if (requestCode == QrScanRequest) {
            val contents = data?.getStringExtra(QrScanActivity.ResultContents)
            if (resultCode == RESULT_OK && !contents.isNullOrBlank()) {
                runCatching { pairingStore.save(PairingConfig.fromQr(contents)) }
                    .onSuccess {
                        status.text = "配对信息已保存，正在自动连接 Windows。"
                        startRelayService()
                    }
                    .onFailure { status.text = "二维码无效：${it.message ?: "无法解析"}" }
            } else {
                status.text = "未扫描到有效的 LyricRelay 配对二维码。"
            }
            return
        }

        super.onActivityResult(requestCode, resultCode, data)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != CameraPermissionRequest) return

        if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) {
            startQrScanner()
        } else {
            status.text = "未获得相机权限，请在系统设置中允许相机访问。"
        }
    }

    private fun updateStatus() {
        status.text = if (isNotificationListenerEnabled()) {
            if (pairingStore.read() == null) "媒体授权已完成，请扫描 Windows 配对二维码。" else "媒体授权和配对信息已准备。"
        } else {
            "尚未授权读取媒体状态。"
        }
    }

    private fun isNotificationListenerEnabled(): Boolean {
        val enabled = Settings.Secure.getString(contentResolver, "enabled_notification_listeners") ?: return false
        return enabled.contains(packageName)
    }

    private fun startRelayService() {
        val intent = Intent(this, RelayService::class.java)
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
    }

    private fun startQrScanner() {
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.CAMERA), CameraPermissionRequest)
            return
        }

        startActivityForResult(Intent(this, QrScanActivity::class.java), QrScanRequest)
    }
}
