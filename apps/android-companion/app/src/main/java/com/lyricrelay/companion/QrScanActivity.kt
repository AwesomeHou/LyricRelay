package com.lyricrelay.companion

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.ImageFormat
import android.graphics.SurfaceTexture
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.media.Image
import android.media.ImageReader
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.view.Gravity
import android.view.Surface
import android.view.TextureView
import android.widget.FrameLayout
import android.widget.TextView
import com.google.zxing.BinaryBitmap
import com.google.zxing.MultiFormatReader
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer

class QrScanActivity : Activity() {
    companion object {
        const val ResultContents = "lyricrelay.qr.contents"
    }

    private lateinit var textureView: TextureView
    private lateinit var status: TextView
    private lateinit var cameraManager: CameraManager
    private var cameraDevice: CameraDevice? = null
    private var captureSession: CameraCaptureSession? = null
    private var imageReader: ImageReader? = null
    private var cameraThread: HandlerThread? = null
    private var cameraHandler: Handler? = null
    @Volatile private var resultDelivered = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            setResult(RESULT_CANCELED)
            finish()
            return
        }

        textureView = TextureView(this)
        status = TextView(this).apply {
            text = "将 Windows 配对二维码放入取景框"
            setTextColor(Color.WHITE)
            setBackgroundColor(0x99000000.toInt())
            setPadding(24, 16, 24, 16)
            gravity = Gravity.CENTER
        }
        setContentView(FrameLayout(this).apply {
            addView(textureView, FrameLayout.LayoutParams(-1, -1))
            addView(status, FrameLayout.LayoutParams(-1, -2, Gravity.BOTTOM))
        })

        cameraManager = getSystemService(Context.CAMERA_SERVICE) as CameraManager
        textureView.surfaceTextureListener = surfaceTextureListener
    }

    override fun onResume() {
        super.onResume()
        cameraThread = HandlerThread("LyricRelayCamera").also { it.start() }
        cameraHandler = Handler(cameraThread!!.looper)
        if (textureView.isAvailable) openCamera()
    }

    override fun onPause() {
        closeCamera()
        cameraThread?.quitSafely()
        cameraThread = null
        cameraHandler = null
        super.onPause()
    }

    private val surfaceTextureListener = object : TextureView.SurfaceTextureListener {
        override fun onSurfaceTextureAvailable(surface: SurfaceTexture, width: Int, height: Int) = openCamera()
        override fun onSurfaceTextureSizeChanged(surface: SurfaceTexture, width: Int, height: Int) = Unit
        override fun onSurfaceTextureDestroyed(surface: SurfaceTexture): Boolean = true
        override fun onSurfaceTextureUpdated(surface: SurfaceTexture) = Unit
    }

    private fun openCamera() {
        try {
            val cameraId = cameraManager.cameraIdList.firstOrNull { id ->
                cameraManager.getCameraCharacteristics(id)
                    .get(CameraCharacteristics.LENS_FACING) == CameraCharacteristics.LENS_FACING_BACK
            } ?: cameraManager.cameraIdList.firstOrNull()

            if (cameraId.isNullOrBlank()) {
                showError("未找到可用摄像头")
                return
            }
            if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
                showError("未获得相机权限")
                return
            }
            cameraManager.openCamera(cameraId, cameraStateCallback, cameraHandler)
        } catch (exception: Exception) {
            showError("相机启动失败：${exception.message ?: "未知错误"}")
        }
    }

    private val cameraStateCallback = object : CameraDevice.StateCallback() {
        override fun onOpened(camera: CameraDevice) {
            cameraDevice = camera
            startPreview(camera)
        }

        override fun onDisconnected(camera: CameraDevice) {
            camera.close()
            cameraDevice = null
            showError("摄像头连接已断开")
        }

        override fun onError(camera: CameraDevice, error: Int) {
            camera.close()
            cameraDevice = null
            showError("相机启动失败，错误码：$error")
        }
    }

    private fun startPreview(camera: CameraDevice) {
        val surfaceTexture = textureView.surfaceTexture ?: return
        val width = 1280
        val height = 720
        surfaceTexture.setDefaultBufferSize(width, height)
        val previewSurface = Surface(surfaceTexture)
        imageReader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 2).also { reader ->
            reader.setOnImageAvailableListener({ source ->
                source.acquireLatestImage()?.let { image -> analyze(image) }
            }, cameraHandler)
        }

        try {
            val request = camera.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW).apply {
                addTarget(previewSurface)
                addTarget(imageReader!!.surface)
            }
            camera.createCaptureSession(
                listOf(previewSurface, imageReader!!.surface),
                object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(session: CameraCaptureSession) {
                        captureSession = session
                        runCatching {
                            session.setRepeatingRequest(request.build(), null, cameraHandler)
                        }.onFailure { showError("相机预览启动失败：${it.message ?: "未知错误"}") }
                    }

                    override fun onConfigureFailed(session: CameraCaptureSession) {
                        showError("相机预览配置失败")
                    }
                },
                cameraHandler
            )
        } catch (exception: Exception) {
            showError("相机预览启动失败：${exception.message ?: "未知错误"}")
        }
    }

    private fun analyze(image: Image) {
        try {
            if (resultDelivered) return
            decode(image)?.let { contents ->
                resultDelivered = true
                runOnUiThread {
                    setResult(RESULT_OK, Intent().putExtra(ResultContents, contents))
                    finish()
                }
            }
        } finally {
            image.close()
        }
    }

    private fun decode(image: Image): String? {
        val plane = image.planes.firstOrNull() ?: return null
        val width = image.width
        val height = image.height
        val rowStride = plane.rowStride
        val buffer = plane.buffer.duplicate()
        val luminance = ByteArray(width * height)
        for (row in 0 until height) {
            buffer.position(row * rowStride)
            buffer.get(luminance, row * width, width)
        }

        return try {
            val source = PlanarYUVLuminanceSource(luminance, width, height, 0, 0, width, height, false)
            MultiFormatReader().decode(BinaryBitmap(HybridBinarizer(source))).text
        } catch (_: Exception) {
            null
        }
    }

    private fun showError(message: String) {
        runOnUiThread { status.text = message }
    }

    private fun closeCamera() {
        captureSession?.close()
        captureSession = null
        cameraDevice?.close()
        cameraDevice = null
        imageReader?.close()
        imageReader = null
    }
}
