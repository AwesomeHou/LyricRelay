plugins {
    id("com.android.application")
    kotlin("android")
}

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
}

dependencies {
    implementation("com.google.zxing:core:3.5.3")
}
