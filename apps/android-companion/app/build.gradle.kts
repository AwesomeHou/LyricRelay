plugins {
    id("com.android.application")
    kotlin("android")
}

val compatibleDebugStoreFile = providers.gradleProperty("lyricrelayDebugStoreFile").orNull
val compatibleDebugStorePassword = providers.gradleProperty("lyricrelayDebugStorePassword").orElse("android").get()
val compatibleDebugKeyAlias = providers.gradleProperty("lyricrelayDebugKeyAlias").orElse("androiddebugkey").get()
val compatibleDebugKeyPassword = providers.gradleProperty("lyricrelayDebugKeyPassword").orElse("android").get()

android {
    namespace = "com.lyricrelay.companion"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.lyricrelay.companion"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0"
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

    if (!compatibleDebugStoreFile.isNullOrBlank()) {
        signingConfigs {
            create("compatibleDebug") {
                storeFile = file(compatibleDebugStoreFile)
                storePassword = compatibleDebugStorePassword
                keyAlias = compatibleDebugKeyAlias
                keyPassword = compatibleDebugKeyPassword
            }
        }
        buildTypes.getByName("debug") {
            signingConfig = signingConfigs.getByName("compatibleDebug")
        }
    }
}

dependencies {
    implementation("com.google.zxing:core:3.5.3")
}
