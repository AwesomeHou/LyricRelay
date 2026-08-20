package com.lyricrelay.companion

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.WindowInsets
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import kotlin.math.roundToInt

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
        val root = ScrollView(this).apply {
            isFillViewport = true
            setBackgroundColor(Color.rgb(245, 247, 246))
        }
        val horizontalPadding = dp(24)
        val topPadding = dp(28)
        val bottomPadding = dp(24)
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(horizontalPadding, topPadding, horizontalPadding, bottomPadding)
        }
        root.addView(content, FrameLayout.LayoutParams(-1, -2))
        root.setOnApplyWindowInsetsListener { _, insets ->
            val safeInsets = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
            } else {
                null
            }
            val topInset = safeInsets?.top ?: insets.systemWindowInsetTop
            val bottomInset = safeInsets?.bottom ?: insets.systemWindowInsetBottom
            content.setPadding(horizontalPadding, topPadding + topInset, horizontalPadding, bottomPadding + bottomInset)
            insets
        }

        content.addView(TextView(this).apply {
            text = "Lyric Relay"
            textSize = 30f
            setTextColor(Color.rgb(31, 41, 51))
            setTypeface(Typeface.DEFAULT, Typeface.BOLD)
        })
        content.addView(TextView(this).apply {
            text = "授权媒体访问后，扫描 Windows 端二维码即可开始同步。"
            textSize = 16f
            setTextColor(Color.rgb(102, 114, 125))
            setPadding(0, dp(8), 0, 0)
        }, marginParams(bottom = 22))

        val statusPanel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(18), dp(16), dp(18), dp(16))
            background = roundedBackground(Color.rgb(231, 243, 241), Color.rgb(184, 217, 212))
        }
        statusPanel.addView(TextView(this).apply {
            text = "当前状态"
            textSize = 12f
            setTextColor(Color.rgb(15, 118, 110))
            setTypeface(Typeface.DEFAULT, Typeface.BOLD)
        })
        status = TextView(this).apply {
            textSize = 15f
            setTextColor(Color.rgb(31, 41, 51))
            setTypeface(Typeface.DEFAULT, Typeface.BOLD)
        }
        statusPanel.addView(status, marginParams(top = 6))
        content.addView(statusPanel, marginParams(bottom = 24))

        content.addView(TextView(this).apply {
            text = "需要完成"
            textSize = 12f
            setTextColor(Color.rgb(15, 118, 110))
            setTypeface(Typeface.DEFAULT, Typeface.BOLD)
        }, marginParams(bottom = 8))

        content.addView(primaryButton("打开媒体访问授权") {
            startActivity(Intent("android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS"))
        }, marginParams(bottom = 10))
        content.addView(secondaryButton("扫描 Windows 配对二维码") {
            startQrScanner()
        })

        content.addView(TextView(this).apply {
            text = "仅同步播放状态，不传输手机音频。"
            textSize = 13f
            setTextColor(Color.rgb(102, 114, 125))
        }, marginParams(top = 28))
        setContentView(root)
        root.requestApplyInsets()
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

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).roundToInt()

    private fun marginParams(top: Int = 0, bottom: Int = 0): LinearLayout.LayoutParams =
        LinearLayout.LayoutParams(-1, -2).apply {
            setMargins(0, dp(top), 0, dp(bottom))
        }

    private fun roundedBackground(fill: Int, stroke: Int? = null): GradientDrawable =
        GradientDrawable().apply {
            setColor(fill)
            cornerRadius = dp(12).toFloat()
            if (stroke != null) setStroke(dp(1), stroke)
        }

    private fun primaryButton(label: String, action: () -> Unit): Button =
        Button(this).apply {
            text = label
            isAllCaps = false
            textSize = 15f
            setTextColor(Color.WHITE)
            minHeight = dp(52)
            setPadding(dp(16), 0, dp(16), 0)
            background = roundedBackground(Color.rgb(15, 118, 110))
            setOnClickListener { action() }
        }

    private fun secondaryButton(label: String, action: () -> Unit): Button =
        Button(this).apply {
            text = label
            isAllCaps = false
            textSize = 15f
            setTextColor(Color.rgb(31, 41, 51))
            minHeight = dp(52)
            setPadding(dp(16), 0, dp(16), 0)
            background = roundedBackground(Color.WHITE, Color.rgb(213, 221, 218))
            setOnClickListener { action() }
        }
}
