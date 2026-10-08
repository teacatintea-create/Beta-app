@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)

package com.practiceboard.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.practiceboard.app.ai.ClaudeTaskGenerator
import com.practiceboard.app.data.DifficultyMode
import com.practiceboard.app.data.Repository
import com.practiceboard.app.data.Sphere
import com.practiceboard.app.data.SphereSettings
import com.practiceboard.app.data.TaskBase
import kotlin.math.roundToInt

@Composable
fun SettingsScreen(repo: Repository, version: Int) {
    val context = LocalContext.current
    val settings = remember(version) { repo.settings() }
    var confirmReset by remember { mutableStateOf(false) }

    LazyColumn(
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        item {
            Text("Настройки", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Text(
                "Сколько заданий и какой сложности появляется на доске каждый день. " +
                    "Доски на сегодня и следующие дни, где ещё ничего не выполнено, обновятся сразу.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        items(Sphere.entries) { sphere ->
            SphereSettingsCard(sphere, settings.sphere(sphere)) { updated ->
                repo.saveSettings(settings.copy(spheres = settings.spheres + (sphere to updated)))
            }
        }
        item {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(14.dp)) {
                    var percent by remember(settings.generatorPercent) { mutableFloatStateOf(settings.generatorPercent.toFloat()) }
                    Text("🎲 Доля заданий от генератора: ${percent.roundToInt()}%", fontWeight = FontWeight.Bold)
                    Text(
                        "Остальные задания берутся из базы (${TaskBase.all.size} заданий). Генератор даёт бесконечно новые идеи.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Slider(
                        value = percent,
                        onValueChange = { percent = it },
                        valueRange = 0f..100f,
                        steps = 9,
                        onValueChangeFinished = { repo.saveSettings(settings.copy(generatorPercent = percent.roundToInt())) },
                    )
                }
            }
        }
        item {
            AiSettingsCard(repo, settings)
        }
        item {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(14.dp)) {
                    Text("📱 Виджет", fontWeight = FontWeight.Bold)
                    Text(
                        "Долгое нажатие на главном экране → «Виджеты» → «Доска практики». " +
                            "Стрелки листают доски по дням, нажатие на задание отмечает его выполненным, «↗» открывает приложение.",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
            }
        }
        item {
            OutlinedButton(onClick = { confirmReset = true }, modifier = Modifier.fillMaxWidth()) {
                Text("Сбросить доски и прогресс")
            }
        }
    }

    if (confirmReset) {
        AlertDialog(
            onDismissRequest = { confirmReset = false },
            title = { Text("Сбросить прогресс?") },
            text = { Text("Удалятся все доски, отметки о выполнении и изученные темы. Настройки и «Мои задания» останутся.") },
            confirmButton = {
                TextButton(onClick = {
                    repo.resetProgress()
                    confirmReset = false
                    toast(context, "Прогресс сброшен")
                }) { Text("Сбросить") }
            },
            dismissButton = { TextButton(onClick = { confirmReset = false }) { Text("Отмена") } },
        )
    }
}

@Composable
private fun SphereSettingsCard(sphere: Sphere, value: SphereSettings, onChange: (SphereSettings) -> Unit) {
    var count by remember(value.count) { mutableFloatStateOf(value.count.toFloat()) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(14.dp)) {
            Text("${sphere.emoji} ${sphere.title}", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(
                if (count.roundToInt() == 0) "Выключено — заданий этой сферы на доске не будет" else "Заданий в день: ${count.roundToInt()}",
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(top = 4.dp),
            )
            Slider(
                value = count,
                onValueChange = { count = it },
                valueRange = 0f..5f,
                steps = 4,
                onValueChangeFinished = { onChange(value.copy(count = count.roundToInt())) },
            )
            Text("Сложность", style = MaterialTheme.typography.labelLarge)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                DifficultyMode.entries.forEach { mode ->
                    FilterChip(
                        selected = value.mode == mode,
                        onClick = { onChange(value.copy(mode = mode)) },
                        label = { Text(mode.title) },
                    )
                }
            }
            if (value.mode == DifficultyMode.WEEKLY) {
                Text(
                    "Пн–Вт — легко, Ср–Пт — средне, выходные — большие задания.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun AiSettingsCard(repo: Repository, settings: com.practiceboard.app.data.AppSettings) {
    val context = LocalContext.current
    var key by remember(settings.apiKey) { mutableStateOf(settings.apiKey) }
    var showKey by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("✨ ИИ-наставник (Claude)", fontWeight = FontWeight.Bold)
                    Text(
                        "Придумывает задания по твоей теме. Нужен интернет и API-ключ Anthropic (console.anthropic.com). Запросы платные по тарифам Anthropic.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Switch(checked = settings.aiEnabled, onCheckedChange = { repo.saveSettings(settings.copy(aiEnabled = it)) })
            }
            if (settings.aiEnabled) {
                OutlinedTextField(
                    value = key,
                    onValueChange = { key = it.trim() },
                    label = { Text("API-ключ") },
                    placeholder = { Text("sk-ant-…") },
                    singleLine = true,
                    visualTransformation = if (showKey) VisualTransformation.None else PasswordVisualTransformation(),
                    trailingIcon = {
                        TextButton(onClick = { showKey = !showKey }) { Text(if (showKey) "Скрыть" else "Показать") }
                    },
                    modifier = Modifier.fillMaxWidth(),
                )
                Button(
                    enabled = key != settings.apiKey,
                    onClick = {
                        repo.saveSettings(settings.copy(apiKey = key))
                        toast(context, "Ключ сохранён")
                    },
                ) { Text("Сохранить ключ") }
                Text("Модель", style = MaterialTheme.typography.labelLarge)
                ClaudeTaskGenerator.MODELS.forEach { option ->
                    FilterChip(
                        selected = settings.aiModel == option.id,
                        onClick = { repo.saveSettings(settings.copy(aiModel = option.id)) },
                        label = { Text(option.title) },
                    )
                }
                Text(
                    "Ключ хранится только на этом телефоне и отправляется лишь в Anthropic.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}
