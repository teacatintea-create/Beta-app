package com.focustube.app

import android.annotation.SuppressLint
import android.content.ActivityNotFoundException
import android.content.Intent
import android.graphics.Bitmap
import android.net.Uri
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.webkit.CookieManager
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.FrameLayout
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import org.json.JSONObject

/**
 * Главный экран: мобильный YouTube (m.youtube.com) во встроенном браузере,
 * в который встраивается тот же JS-фильтр, что и в расширении для ПК.
 */
class MainActivity : ComponentActivity() {

    private lateinit var webView: WebView
    private lateinit var root: FrameLayout
    private var fullscreenView: View? = null
    private var fullscreenCallback: WebChromeClient.CustomViewCallback? = null
    private var documentStartSupported = false
    private lateinit var script: String

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        script = buildInjectedScript()

        root = FrameLayout(this)
        webView = WebView(this)
        root.fitsSystemWindows = true
        root.addView(webView, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        setContentView(root)

        with(webView.settings) {
            javaScriptEnabled = true
            domStorageEnabled = true
            mediaPlaybackRequiresUserGesture = false
            loadWithOverviewMode = true
            useWideViewPort = true
            mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
        }
        CookieManager.getInstance().setAcceptCookie(true)
        CookieManager.getInstance().setAcceptThirdPartyCookies(webView, true)

        webView.addJavascriptInterface(JsBridge(applicationContext) { openSettings() }, "FocusTubeAndroid")

        // Лучший вариант — скрипт запускается в самом начале загрузки страницы.
        if (WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
            WebViewCompat.addDocumentStartJavaScript(webView, script, ALLOWED_ORIGINS)
            documentStartSupported = true
        }

        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val uri = request.url
                if (isInternal(uri)) return false
                openExternally(uri)
                return true
            }

            override fun onPageStarted(view: WebView, url: String?, favicon: Bitmap?) {
                if (!documentStartSupported) view.evaluateJavascript(script, null)
            }

            override fun onPageFinished(view: WebView, url: String?) {
                if (!documentStartSupported) view.evaluateJavascript(script, null)
            }
        }

        webView.webChromeClient = object : WebChromeClient() {
            override fun onShowCustomView(view: View, callback: CustomViewCallback) {
                enterFullscreen(view, callback)
            }

            override fun onHideCustomView() {
                exitFullscreen()
            }
        }

        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                when {
                    fullscreenView != null -> exitFullscreen()
                    webView.canGoBack() -> webView.goBack()
                    else -> finish()
                }
            }
        })

        val restored = savedInstanceState != null && webView.restoreState(savedInstanceState) != null
        if (!restored) webView.loadUrl(urlFromIntent(intent) ?: HOME_URL)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        urlFromIntent(intent)?.let { webView.loadUrl(it) }
    }

    override fun onResume() {
        super.onResume()
        webView.onResume()
        // Настройки могли поменяться на экране настроек — сообщаем фильтру.
        val json = JSONObject.quote(Store.settings(this).toString())
        webView.evaluateJavascript(
            "window.FT && FT.platform && FT.platform._emit && FT.platform._emit(JSON.parse($json));",
            null,
        )
    }

    override fun onPause() {
        webView.onPause()
        super.onPause()
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        webView.saveState(outState)
    }

    override fun onDestroy() {
        webView.destroy()
        super.onDestroy()
    }

    private fun openSettings() {
        runOnUiThread { startActivity(Intent(this, SettingsActivity::class.java)) }
    }

    private fun isInternal(uri: Uri): Boolean {
        val host = uri.host?.lowercase() ?: return false
        return host == "youtube.com" || host.endsWith(".youtube.com") || host == "youtu.be" ||
            host == "accounts.google.com"
    }

    private fun openExternally(uri: Uri) {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, uri).addCategory(Intent.CATEGORY_BROWSABLE))
        } catch (e: ActivityNotFoundException) {
            // Нечем открыть — просто игнорируем.
        }
    }

    /** Приводит ссылку на YouTube к мобильной версии сайта. */
    private fun urlFromIntent(intent: Intent?): String? {
        if (intent == null) return null
        val raw = when (intent.action) {
            Intent.ACTION_VIEW -> intent.dataString
            Intent.ACTION_SEND -> intent.getStringExtra(Intent.EXTRA_TEXT)
                ?.let { Regex("https?://\\S+").find(it)?.value }
            else -> null
        } ?: return null
        val uri = Uri.parse(raw)
        val host = uri.host?.lowercase() ?: return null
        if (host == "youtu.be") {
            val id = uri.lastPathSegment ?: return HOME_URL
            return "https://m.youtube.com/watch?v=$id"
        }
        if (host == "youtube.com" || host.endsWith(".youtube.com")) {
            return uri.buildUpon().scheme("https").authority("m.youtube.com").build().toString()
        }
        return null
    }

    private fun enterFullscreen(view: View, callback: WebChromeClient.CustomViewCallback) {
        if (fullscreenView != null) {
            callback.onCustomViewHidden()
            return
        }
        fullscreenView = view
        fullscreenCallback = callback
        root.fitsSystemWindows = false
        root.setPadding(0, 0, 0, 0)
        root.addView(view, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        webView.visibility = View.GONE
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    private fun exitFullscreen() {
        val view = fullscreenView ?: return
        root.removeView(view)
        fullscreenView = null
        fullscreenCallback?.onCustomViewHidden()
        fullscreenCallback = null
        webView.visibility = View.VISIBLE
        root.fitsSystemWindows = true
        WindowCompat.setDecorFitsSystemWindows(window, true)
        WindowInsetsControllerCompat(window, window.decorView).show(WindowInsetsCompat.Type.systemBars())
        root.requestApplyInsets()
    }

    /**
     * Собирает один скрипт из общих файлов фильтра (папка extension/src
     * подключена как assets) и платформенного слоя для Android.
     */
    private fun buildInjectedScript(): String {
        fun asset(name: String) = assets.open(name).bufferedReader().use { it.readText() }
        val css = JSONObject.quote(asset("content.css"))
        val parts = listOf("defaults.js", "classifier.js", "metadata.js", "platform-android.js", "content.js")
            .joinToString("\n;\n") { asset(it) }
        return """
            (function () {
              if (window.__focusTubeBoot) return;
              window.__focusTubeBoot = true;
              var css = $css;
              function addCss() {
                if (!document.documentElement) return setTimeout(addCss, 10);
                if (document.getElementById('focustube-css')) return;
                var st = document.createElement('style');
                st.id = 'focustube-css';
                st.textContent = css;
                (document.head || document.documentElement).appendChild(st);
              }
              addCss();
              document.addEventListener('DOMContentLoaded', addCss);
              var tries = 0;
              (function boot() {
                if (!window.FocusTubeAndroid) {
                  if (tries++ < 100) setTimeout(boot, 50);
                  return;
                }
                $parts
              })();
            })();
        """.trimIndent()
    }

    companion object {
        const val HOME_URL = "https://m.youtube.com/"
        val ALLOWED_ORIGINS = setOf("https://m.youtube.com", "https://www.youtube.com", "https://youtube.com")
    }
}
