package com.focustube.app

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDate

/**
 * Хранилище настроек и дневной статистики.
 *
 * Настройки лежат одной JSON-строкой в том же формате, что и в расширении для
 * браузера (см. extension/src/defaults.js), поэтому JS-фильтр читает их как есть.
 */
object Store {
    private const val PREFS = "focustube"
    private const val KEY_SETTINGS = "settings"
    private const val KEY_STATS = "stats"

    /** Значения по умолчанию — такие же, как FT.DEFAULTS в defaults.js. */
    fun defaults(): JSONObject = JSONObject().apply {
        put("enabled", true)
        put("mode", "balanced")
        put("allowCreative", true)
        put("allowedCategories", JSONArray(listOf(27, 28)))
        put("creativeCategories", JSONArray(listOf(1, 10, 26)))
        put("hideShorts", true)
        put("hideHomeFeed", true)
        put("hideComments", false)
        put("hideEndScreen", true)
        put("hideMixes", true)
        put("blockWatchPage", true)
        put("dailyLimitMin", 0)
        put("apiKey", "")
        put("whitelistChannels", JSONArray())
        put("blacklistChannels", JSONArray())
        put("extraAllowKeywords", JSONArray())
        put("extraBlockKeywords", JSONArray())
        put("interests", JSONArray(listOf("физика", "программирование", "история", "рисование")))
        put("lockDelaySec", 20)
        put("pausedUntil", 0L)
    }

    private fun prefs(ctx: Context) = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    @Synchronized
    fun settings(ctx: Context): JSONObject {
        val result = defaults()
        val raw = prefs(ctx).getString(KEY_SETTINGS, null) ?: return result
        val stored = try {
            JSONObject(raw)
        } catch (e: Exception) {
            return result
        }
        for (key in stored.keys()) result.put(key, stored.get(key))
        return result
    }

    @Synchronized
    fun saveSettings(ctx: Context, partial: JSONObject) {
        val current = settings(ctx)
        for (key in partial.keys()) current.put(key, partial.get(key))
        prefs(ctx).edit().putString(KEY_SETTINGS, current.toString()).apply()
    }

    @Synchronized
    fun resetSettings(ctx: Context) {
        prefs(ctx).edit().remove(KEY_SETTINGS).apply()
    }

    private fun today(): String = LocalDate.now().toString()

    private fun emptyStats() = JSONObject().apply {
        put("date", today())
        put("watchSeconds", 0)
        put("hidden", 0)
        put("blocked", 0)
        put("allowed", 0)
    }

    @Synchronized
    fun stats(ctx: Context): JSONObject {
        val raw = prefs(ctx).getString(KEY_STATS, null) ?: return emptyStats()
        return try {
            val s = JSONObject(raw)
            if (s.optString("date") == today()) s else emptyStats()
        } catch (e: Exception) {
            emptyStats()
        }
    }

    @Synchronized
    fun addStats(ctx: Context, delta: JSONObject): JSONObject {
        val s = stats(ctx)
        for (key in listOf("watchSeconds", "hidden", "blocked", "allowed")) {
            s.put(key, s.optInt(key) + delta.optInt(key))
        }
        prefs(ctx).edit().putString(KEY_STATS, s.toString()).apply()
        return s
    }
}

/** Удобные преобразования списков строк <-> JSONArray. */
fun JSONObject.stringList(key: String): List<String> {
    val arr = optJSONArray(key) ?: return emptyList()
    return (0 until arr.length()).map { arr.optString(it) }.filter { it.isNotBlank() }
}

fun List<String>.toJsonArray(): JSONArray = JSONArray(this)
