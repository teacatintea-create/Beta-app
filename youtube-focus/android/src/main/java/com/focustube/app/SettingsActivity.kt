package com.focustube.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import org.json.JSONObject

class SettingsActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            FocusTheme {
                Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    SettingsScreen(onClose = { finish() })
                }
            }
        }
    }
}

@Composable
fun FocusTheme(content: @Composable () -> Unit) {
    val accent = Color(0xFF3B82F6)
    val scheme = if (isSystemInDarkTheme()) darkColorScheme(primary = accent) else lightColorScheme(primary = accent)
    MaterialTheme(colorScheme = scheme, content = content)
}

private val MODES = listOf(
    "strict" to "Строгий — только явно образовательное",
    "balanced" to "Сбалансированный — нужна хотя бы одна полезная примета",
    "soft" to "Мягкий — скрывать только явное развлекалово",
)
private val STRICTNESS = mapOf("strict" to 2, "balanced" to 1, "soft" to 0)

private val SWITCHES = listOf(
    "allowCreative" to "Творчество тоже считается полезным",
    "blockWatchPage" to "Проверять открытый ролик и закрывать его заставкой",
    "hideHomeFeed" to "Убрать ленту рекомендаций с главной",
    "hideShorts" to "Скрыть Shorts",
    "hideMixes" to "Скрыть миксы и автоплейлисты",
    "hideEndScreen" to "Скрыть подсказки в конце видео",
    "hideComments" to "Скрыть комментарии",
)

private class Delayed(val label: String, val action: () -> Unit)

@Composable
fun SettingsScreen(onClose: () -> Unit) {
    val ctx = LocalContext.current
    var version by remember { mutableIntStateOf(0) }
    val s = remember(version) { Store.settings(ctx) }
    val stats = remember(version) { Store.stats(ctx) }
    var delayed by remember { mutableStateOf<Delayed?>(null) }

    fun save(key: String, value: Any) {
        Store.saveSettings(ctx, JSONObject().put(key, value))
        version++
    }

    fun withDelay(label: String, action: () -> Unit) {
        if (s.optInt("lockDelaySec") <= 0) action() else delayed = Delayed(label, action)
    }

    val enabled = s.optBoolean("enabled", true)
    val pausedUntil = s.optLong("pausedUntil")
    val paused = enabled && System.currentTimeMillis() < pausedUntil

    Column(
        modifier = Modifier
            .fillMaxSize()
            .safeDrawingPadding()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("FocusTube", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            TextButton(onClick = onClose) { Text("Готово") }
        }

        Card(modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp)) {
                val minutes = stats.optInt("watchSeconds") / 60
                val limit = s.optInt("dailyLimitMin")
                Text("Сегодня", fontWeight = FontWeight.SemiBold)
                Text("На YouTube: $minutes мин" + (if (limit > 0) " из $limit" else ""))
                Text("Скрыто роликов: ${stats.optInt("hidden")} · заблокировано видео: ${stats.optInt("blocked")}")
            }
        }

        Section("Фильтр")
        SwitchRow(
            title = when {
                !enabled -> "Фильтр выключен"
                paused -> "Фильтр на паузе"
                else -> "Фильтр включён"
            },
            checked = enabled && !paused,
        ) { on ->
            if (on) {
                Store.saveSettings(ctx, JSONObject().put("enabled", true).put("pausedUntil", 0L))
                version++
            } else {
                withDelay("Выключить фильтр") { save("enabled", false) }
            }
        }
        if (enabled && !paused) {
            OutlinedButton(onClick = {
                withDelay("Поставить на паузу на 15 минут") {
                    save("pausedUntil", System.currentTimeMillis() + 15 * 60 * 1000L)
                }
            }) { Text("Пауза на 15 минут") }
        }

        Text("Режим", fontWeight = FontWeight.SemiBold)
        val mode = s.optString("mode", "balanced")
        MODES.forEach { (value, label) ->
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable {
                        if (value == mode) return@clickable
                        if ((STRICTNESS[value] ?: 0) < (STRICTNESS[mode] ?: 0)) {
                            withDelay("Сделать фильтр мягче") { save("mode", value) }
                        } else {
                            save("mode", value)
                        }
                    },
            ) {
                RadioButton(selected = value == mode, onClick = null)
                Spacer(Modifier.width(8.dp))
                Text(label)
            }
        }

        SWITCHES.forEach { (key, label) ->
            SwitchRow(title = label, checked = s.optBoolean(key)) { save(key, it) }
        }

        Section("Время")
        NumberField("Лимит в день, минут (0 — без лимита)", s.optInt("dailyLimitMin")) { save("dailyLimitMin", it) }
        NumberField("Задержка перед отключением, секунд", s.optInt("lockDelaySec")) { value ->
            // Уменьшать задержку — тоже ослабление защиты.
            if (value < s.optInt("lockDelaySec")) withDelay("Уменьшить задержку") { save("lockDelaySec", value) }
            else save("lockDelaySec", value)
        }

        Section("Списки")
        ListField("Мои интересы (по одному на строку)", s.stringList("interests"), version) { save("interests", it.toJsonArray()) }
        ListField("Белый список каналов (название или @handle)", s.stringList("whitelistChannels"), version) {
            save("whitelistChannels", it.toJsonArray())
        }
        ListField("Чёрный список каналов", s.stringList("blacklistChannels"), version) { save("blacklistChannels", it.toJsonArray()) }
        ListField("Разрешённые слова (+3 балла)", s.stringList("extraAllowKeywords"), version) { save("extraAllowKeywords", it.toJsonArray()) }
        ListField("Запрещённые слова (−5 баллов)", s.stringList("extraBlockKeywords"), version) { save("extraBlockKeywords", it.toJsonArray()) }

        Section("YouTube Data API (необязательно)")
        Text(
            "С ключом API фильтр видит категорию и теги каждого ролика в ленте — так точнее. " +
                "Ключ бесплатный: Google Cloud Console → включить «YouTube Data API v3» → Credentials → Create API key.",
            style = MaterialTheme.typography.bodySmall,
        )
        var apiKey by remember { mutableStateOf(s.optString("apiKey")) }
        OutlinedTextField(
            value = apiKey,
            onValueChange = {
                apiKey = it.trim()
                save("apiKey", apiKey)
            },
            label = { Text("API-ключ") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = {
            withDelay("Сбросить все настройки") {
                Store.resetSettings(ctx)
                version++
            }
        }) { Text("Сбросить настройки") }
        Spacer(Modifier.height(24.dp))
    }

    delayed?.let { d ->
        DelayDialog(
            label = d.label,
            seconds = s.optInt("lockDelaySec"),
            onConfirm = {
                delayed = null
                d.action()
            },
            onCancel = { delayed = null },
        )
    }
}

@Composable
private fun Section(title: String) {
    Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 8.dp))
}

@Composable
private fun SwitchRow(title: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clickable { onChange(!checked) },
    ) {
        Text(title, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

@Composable
private fun NumberField(label: String, value: Int, onChange: (Int) -> Unit) {
    var text by remember(value) { mutableStateOf(value.toString()) }
    OutlinedTextField(
        value = text,
        onValueChange = { input ->
            text = input.filter { it.isDigit() }.take(4)
        },
        label = { Text(label) },
        singleLine = true,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        modifier = Modifier.fillMaxWidth(),
        trailingIcon = {
            val parsed = text.toIntOrNull() ?: 0
            if (parsed != value) TextButton(onClick = { onChange(parsed) }) { Text("OK") }
        },
    )
}

@Composable
private fun ListField(label: String, items: List<String>, version: Int, onChange: (List<String>) -> Unit) {
    // Текст храним отдельно, чтобы можно было спокойно печатать переносы строк.
    var text by remember(label) { mutableStateOf(items.joinToString("\n")) }
    LaunchedEffect(version) {
        val current = text.lines().map { it.trim() }.filter { it.isNotEmpty() }
        if (current != items) text = items.joinToString("\n")
    }
    OutlinedTextField(
        value = text,
        onValueChange = {
            text = it
            onChange(it.lines().map { line -> line.trim() }.filter { line -> line.isNotEmpty() })
        },
        label = { Text(label) },
        minLines = 2,
        modifier = Modifier.fillMaxWidth(),
    )
}

@Composable
private fun DelayDialog(label: String, seconds: Int, onConfirm: () -> Unit, onCancel: () -> Unit) {
    var left by remember { mutableIntStateOf(seconds) }
    LaunchedEffect(Unit) {
        while (left > 0) {
            delay(1000)
            left--
        }
    }
    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text(label + "?") },
        text = {
            Text(
                if (left > 0) "Подожди $left с. Это время, чтобы передумать: точно нужно ослаблять фильтр?"
                else "Можно подтверждать."
            )
        },
        confirmButton = {
            Button(onClick = onConfirm, enabled = left <= 0) { Text(if (left > 0) "Подтвердить ($left)" else "Подтвердить") }
        },
        dismissButton = { TextButton(onClick = onCancel) { Text("Передумал") } },
    )
}
