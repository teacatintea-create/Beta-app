package com.focustube.app

import android.content.Context
import android.webkit.JavascriptInterface
import org.json.JSONObject

/**
 * Мост между JS-фильтром (platform-android.js) и хранилищем приложения.
 * Методы вызываются из JS в фоновом потоке WebView.
 */
class JsBridge(
    private val context: Context,
    private val onOpenSettings: () -> Unit,
) {
    @JavascriptInterface
    fun getSettings(): String = Store.settings(context).toString()

    @JavascriptInterface
    fun saveSettings(partialJson: String) {
        val partial = try {
            JSONObject(partialJson)
        } catch (e: Exception) {
            return
        }
        Store.saveSettings(context, partial)
    }

    @JavascriptInterface
    fun getStats(): String = Store.stats(context).toString()

    @JavascriptInterface
    fun addStats(deltaJson: String): String {
        val delta = try {
            JSONObject(deltaJson)
        } catch (e: Exception) {
            JSONObject()
        }
        return Store.addStats(context, delta).toString()
    }

    @JavascriptInterface
    fun openSettings() = onOpenSettings()
}
