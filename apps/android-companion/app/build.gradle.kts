plugins {
    id("com.android.application")
    kotlin("android")
}

val configuredDebugStoreFile = providers.gradleProperty("lyricrelayDebugStoreFile").orNull
val stableDebugStoreFile = configuredDebugStoreFile?.let(::file)
    ?: rootProject.file("signing/lyricrelay-debug.keystore")
val compatibleDebugStorePassword = providers.gradleProperty("lyricrelayDebugStorePassword").orElse("android").get()
val compatibleDebugKeyAlias = providers.gradleProperty("lyricrelayDebugKeyAlias").orElse("androiddebugkey").get()
val compatibleDebugKeyPassword = providers.gradleProperty("lyricrelayDebugKeyPassword").orElse("android").get()

if (!stableDebugStoreFile.isFile) {
    throw GradleException(
        "Missing stable Android debug keystore at ${stableDebugStoreFile.absolutePath}. " +
            "Restore that file or set lyricrelayDebugStoreFile to the backed-up keystore."
    )
}

android {
    namespace = "com.lyricrelay.companion"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.lyricrelay.companion"
        minSdk = 26
        targetSdk = 35
        versionCode = providers.gradleProperty("lyricrelayVersionCode").map(String::toInt).orElse(2).get()
        versionName = providers.gradleProperty("lyricrelayVersionName").orElse("0.1.1").get()
    }

    buildFeatures {
        buildConfig = true
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    signingConfigs {
        create("compatibleDebug") {
            storeFile = stableDebugStoreFile
            storePassword = compatibleDebugStorePassword
            keyAlias = compatibleDebugKeyAlias
            keyPassword = compatibleDebugKeyPassword
        }
    }
    buildTypes.getByName("debug") {
        signingConfig = signingConfigs.getByName("compatibleDebug")
    }
}

dependencies {
    implementation("com.google.zxing:core:3.5.3")
}
