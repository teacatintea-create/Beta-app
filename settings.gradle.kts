pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "PracticeBoard"
include(":app")

// FocusTube — Android-приложение «только полезный YouTube» (см. youtube-focus/README.md).
include(":focustube")
project(":focustube").projectDir = file("youtube-focus/android")
