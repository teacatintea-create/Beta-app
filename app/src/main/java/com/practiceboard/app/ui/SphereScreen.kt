@file:OptIn(ExperimentalMaterial3Api::class)

package com.practiceboard.app.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.practiceboard.app.data.Difficulty
import com.practiceboard.app.data.Learning
import com.practiceboard.app.data.LearningTopic
import com.practiceboard.app.data.PracticeTask
import com.practiceboard.app.data.Repository
import com.practiceboard.app.data.Sphere
import com.practiceboard.app.data.TaskBase
import com.practiceboard.app.data.TaskGenerator
import com.practiceboard.app.data.TaskSource
import kotlinx.coroutines.launch
import java.time.LocalDate

@Composable
fun SphereScreen(repo: Repository, version: Int, sphere: Sphere) {
    var tab by rememberSaveable(sphere) { mutableIntStateOf(0) }
    var topicFilter by rememberSaveable(sphere) { mutableStateOf<String?>(null) }

    Column(Modifier.fillMaxSize()) {
        Text(
            "${sphere.emoji} ${sphere.title}",
            style = MaterialTheme.typography.headlineSmall,
            fontWeight = FontWeight.Bold,
            modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 8.dp),
        )
        TabRow(selectedTabIndex = tab) {
            listOf("Задания", "Генератор", "Обучение").forEachIndexed { index, title ->
                Tab(selected = tab == index, onClick = { tab = index }, text = { Text(title) })
            }
        }
        when (tab) {
            0 -> TasksTab(repo, version, sphere, topicFilter) { topicFilter = it }
            1 -> GeneratorTab(repo, version, sphere)
            else -> LearnTab(repo, version, sphere) { topic ->
                topicFilter = topic
                tab = 0
            }
        }
    }
}

@Composable
private fun TasksTab(repo: Repository, version: Int, sphere: Sphere, topic: String?, onTopicChange: (String?) -> Unit) {
    val context = LocalContext.current
    var query by rememberSaveable(sphere) { mutableStateOf("") }
    var level by rememberSaveable(sphere) { mutableIntStateOf(0) }
    var mineOnly by rememberSaveable(sphere) { mutableStateOf(false) }
    val saved = remember(version, sphere) { repo.savedTasks(sphere) }
    val all = remember(saved, sphere) { saved + TaskBase.forSphere(sphere) }
    val topics = remember(sphere) { TaskBase.topics(sphere) }
    val filtered = all.filter { t ->
        (level == 0 || t.difficulty.level == level) &&
            (topic == null || t.topic == topic) &&
            (!mineOnly || t.source != TaskSource.BASE) &&
            (query.isBlank() || t.title.contains(query, ignoreCase = true) || t.description.contains(query, ignoreCase = true))
    }

    LazyColumn(
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        item {
            OutlinedTextField(
                value = query,
                onValueChange = { query = it },
                label = { Text("Поиск по заданиям") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
        }
        item { DifficultyChips(level, allowAll = true) { level = it } }
        item {
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                item {
                    FilterChip(selected = mineOnly, onClick = { mineOnly = !mineOnly }, label = { Text("⭐ Мои (${saved.size})") })
                }
                item {
                    FilterChip(selected = topic == null, onClick = { onTopicChange(null) }, label = { Text("Все темы") })
                }
                items(topics) { t ->
                    FilterChip(
                        selected = topic == t,
                        onClick = { onTopicChange(if (topic == t) null else t) },
                        label = { Text(t) },
                    )
                }
            }
        }
        item {
            Text(
                "Найдено: ${filtered.size} из ${all.size}",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        items(filtered, key = { it.id }) { task ->
            TaskCard(task) {
                if (task.source != TaskSource.BASE) {
                    TextButton(onClick = { repo.deleteSavedTask(task) }) { Text("Удалить") }
                }
                TextButton(onClick = {
                    repo.addTask(LocalDate.now(), task)
                    toast(context, "Добавлено на сегодняшнюю доску")
                }) { Text("На сегодня") }
            }
        }
    }
}

@Composable
private fun GeneratorTab(repo: Repository, version: Int, sphere: Sphere) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val settings = remember(version) { repo.settings() }
    var level by rememberSaveable(sphere) { mutableIntStateOf(1) }
    var generated by remember(sphere) { mutableStateOf<List<PracticeTask>>(emptyList()) }
    var aiTopic by rememberSaveable(sphere) { mutableStateOf("") }
    var aiCount by rememberSaveable(sphere) { mutableIntStateOf(2) }
    var aiLoading by remember { mutableStateOf(false) }
    var aiError by remember { mutableStateOf<String?>(null) }
    var aiResults by remember(sphere) { mutableStateOf<List<PracticeTask>>(emptyList()) }

    val addToday: (PracticeTask) -> Unit = { task ->
        repo.addTask(LocalDate.now(), task)
        toast(context, "Добавлено на сегодняшнюю доску")
    }

    LazyColumn(
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        item {
            Text("🎲 Генератор заданий", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(
                "Собирает задание из тысяч комбинаций: объект, стиль, ограничение, настроение. Работает без интернета.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        item { DifficultyChips(level, allowAll = false) { level = it } }
        item {
            Button(
                onClick = { generated = List(3) { TaskGenerator.generate(sphere, Difficulty.fromLevel(level)) }.distinctBy { it.id } },
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Сгенерировать 3 идеи") }
        }
        items(generated, key = { "g-" + it.id }) { task ->
            TaskCard(task) {
                TextButton(onClick = {
                    repo.saveTask(task)
                    toast(context, "Сохранено в «Мои задания»")
                }) { Text("Сохранить") }
                TextButton(onClick = { addToday(task) }) { Text("На сегодня") }
            }
        }
        item { HorizontalDivider(Modifier.padding(vertical = 6.dp)) }
        item {
            Text("✨ ИИ-наставник (Claude)", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(
                "Опиши, что хочешь потренировать, — ИИ придумает задания под твой уровень. Они сохраняются в «Мои задания» и пополняют базу.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        if (!settings.aiReady) {
            item {
                Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)) {
                    Text(
                        "Чтобы пользоваться ИИ, открой «Настройки», включи ИИ-наставника и вставь API-ключ Anthropic.",
                        modifier = Modifier.padding(14.dp),
                    )
                }
            }
        } else {
            item {
                OutlinedTextField(
                    value = aiTopic,
                    onValueChange = { aiTopic = it },
                    label = { Text("Тема или пожелание (необязательно)") },
                    placeholder = { Text(topicPlaceholder(sphere)) },
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            item {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Сколько заданий:")
                    (1..3).forEach { n ->
                        FilterChip(selected = aiCount == n, onClick = { aiCount = n }, label = { Text("$n") })
                    }
                }
            }
            item {
                Button(
                    enabled = !aiLoading,
                    onClick = {
                        aiLoading = true
                        aiError = null
                        scope.launch {
                            generateWithAi(repo, settings, sphere, Difficulty.fromLevel(level), aiCount, aiTopic)
                                .onSuccess { aiResults = it }
                                .onFailure { aiError = it.message ?: "Неизвестная ошибка" }
                            aiLoading = false
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    if (aiLoading) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                        Spacer(Modifier.size(8.dp))
                        Text("ИИ думает…")
                    } else {
                        Text("Придумать с ИИ")
                    }
                }
            }
            aiError?.let { message ->
                item { Text(message, color = MaterialTheme.colorScheme.error) }
            }
            items(aiResults, key = { "ai-" + it.id }) { task ->
                TaskCard(task) {
                    TextButton(onClick = { addToday(task) }) { Text("На сегодня") }
                }
            }
        }
    }
}

@Composable
private fun LearnTab(repo: Repository, version: Int, sphere: Sphere, onShowTasks: (String) -> Unit) {
    val learned = remember(version) { repo.learnedTopics() }
    val topics = remember(sphere) { Learning.topics(sphere) }
    val guide = Learning.guides.getValue(sphere)
    var showGuide by rememberSaveable(sphere) { mutableStateOf(false) }
    val learnedCount = topics.count { it.id in learned }

    LazyColumn(
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        item {
            Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer)) {
                Column(Modifier.padding(14.dp)) {
                    Text("🗺️ Дорожная карта", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                    Text("Изучено тем: $learnedCount из ${topics.size}", style = MaterialTheme.typography.bodyMedium)
                    LinearProgressIndicator(
                        progress = { if (topics.isEmpty()) 0f else learnedCount.toFloat() / topics.size },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 8.dp),
                    )
                    Text(
                        "Иди сверху вниз. Отмечай тему изученной, когда выполнишь 3–5 заданий по ней.",
                        style = MaterialTheme.typography.bodySmall,
                        modifier = Modifier.padding(top = 8.dp),
                    )
                }
            }
        }
        item {
            Card(Modifier.clickable { showGuide = !showGuide }) {
                Column(Modifier.padding(14.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            "🧭 Как учиться: пошаговый план",
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.Bold,
                            modifier = Modifier.weight(1f),
                        )
                        Text(if (showGuide) "▲" else "▼")
                    }
                    if (showGuide) {
                        guide.steps.forEachIndexed { i, step ->
                            Text("${i + 1}. $step", modifier = Modifier.padding(top = 6.dp))
                        }
                        Text(
                            "Полезные ресурсы",
                            fontWeight = FontWeight.Bold,
                            modifier = Modifier.padding(top = 12.dp),
                        )
                        guide.resources.forEach { r ->
                            Text("• ${r.name} — ${r.note}", modifier = Modifier.padding(top = 4.dp))
                        }
                        Text(
                            "Главные принципы",
                            fontWeight = FontWeight.Bold,
                            modifier = Modifier.padding(top = 12.dp),
                        )
                        Learning.principles.forEach { p ->
                            Text("• $p", modifier = Modifier.padding(top = 4.dp))
                        }
                    } else {
                        Text(
                            "Нажми, чтобы открыть план, ресурсы и принципы обучения",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
        }
        Difficulty.entries.forEach { difficulty ->
            val group = topics.filter { it.difficulty == difficulty }
            if (group.isNotEmpty()) {
                item {
                    Text(
                        "${difficulty.stars} ${difficulty.title}",
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.Bold,
                        modifier = Modifier.padding(top = 6.dp),
                    )
                }
                items(group, key = { it.id }) { topic ->
                    TopicCard(
                        topic = topic,
                        learned = topic.id in learned,
                        onLearned = { repo.setLearned(topic.id, it) },
                        onShowTasks = { onShowTasks(topic.taskTopic) },
                    )
                }
            }
        }
    }
}

@Composable
private fun TopicCard(topic: LearningTopic, learned: Boolean, onLearned: (Boolean) -> Unit, onShowTasks: () -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(start = 4.dp, end = 14.dp, top = 6.dp, bottom = 6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(checked = learned, onCheckedChange = onLearned)
                Column(
                    Modifier
                        .weight(1f)
                        .clickable { expanded = !expanded },
                ) {
                    Text(topic.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                    Text(topic.summary, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Text(if (expanded) "▲" else "▼", modifier = Modifier.clickable { expanded = !expanded })
            }
            if (expanded) {
                Column(Modifier.padding(start = 12.dp, top = 4.dp)) {
                    Text("Что изучить:", fontWeight = FontWeight.Bold)
                    topic.keyPoints.forEach { Text("• $it") }
                    Text("Практика:", fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp))
                    Text(topic.practice)
                    TextButton(onClick = onShowTasks) { Text("Задания по теме «${topic.taskTopic}» →") }
                }
            }
        }
    }
}
