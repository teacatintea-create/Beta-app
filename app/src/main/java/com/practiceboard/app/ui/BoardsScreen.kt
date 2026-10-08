@file:OptIn(ExperimentalMaterial3Api::class)

package com.practiceboard.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.practiceboard.app.data.BoardItem
import com.practiceboard.app.data.Difficulty
import com.practiceboard.app.data.PracticeTask
import com.practiceboard.app.data.Repository
import com.practiceboard.app.data.Sphere
import com.practiceboard.app.data.TaskBase
import com.practiceboard.app.data.TaskGenerator
import com.practiceboard.app.data.TaskSource
import com.practiceboard.app.widget.BoardWidgetProvider
import kotlinx.coroutines.launch
import java.time.LocalDate

@Composable
fun BoardsScreen(repo: Repository, version: Int, openDate: LocalDate?, openRequest: Int) {
    val today = LocalDate.now()
    val days = remember(today) {
        (-Repository.MAX_PAST_DAYS..Repository.MAX_FUTURE_DAYS).map { today.plusDays(it.toLong()) }
    }
    val todayIndex = Repository.MAX_PAST_DAYS
    val pagerState = rememberPagerState(initialPage = todayIndex) { days.size }
    val scope = rememberCoroutineScope()
    val stats = remember(version) { repo.stats() }

    // Открытие конкретного дня из виджета.
    LaunchedEffect(openRequest) {
        val index = openDate?.let { days.indexOf(it) } ?: -1
        if (index >= 0) pagerState.scrollToPage(index)
    }

    Column(Modifier.fillMaxSize()) {
        Column(Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp)) {
            Text("Доски заданий", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.padding(top = 8.dp),
            ) {
                StatChip("🔥", stats.streak.toString(), "дней подряд", Modifier.weight(1f))
                StatChip("✅", stats.totalDone.toString(), "выполнено", Modifier.weight(1f))
                StatChip("📅", stats.activeDays.toString(), "активных дней", Modifier.weight(1f))
            }
        }

        val current = days[pagerState.currentPage]
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 8.dp, vertical = 6.dp),
        ) {
            TextButton(
                enabled = pagerState.currentPage > 0,
                onClick = { scope.launch { pagerState.animateScrollToPage(pagerState.currentPage - 1) } },
            ) { Text("◀", fontSize = 18.sp) }
            Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(BoardWidgetProvider.dayTitle(current, today), fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
                Text(BoardWidgetProvider.dateLine(current), style = MaterialTheme.typography.bodySmall)
            }
            if (current != today) {
                TextButton(onClick = { scope.launch { pagerState.animateScrollToPage(todayIndex) } }) { Text("Сегодня") }
            }
            TextButton(
                enabled = pagerState.currentPage < days.lastIndex,
                onClick = { scope.launch { pagerState.animateScrollToPage(pagerState.currentPage + 1) } },
            ) { Text("▶", fontSize = 18.sp) }
        }

        HorizontalPager(
            state = pagerState,
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
            contentPadding = PaddingValues(horizontal = 16.dp),
            pageSpacing = 12.dp,
        ) { page ->
            BoardPage(repo, version, days[page])
        }
    }
}

@Composable
private fun StatChip(emoji: String, value: String, label: String, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(14.dp),
        color = MaterialTheme.colorScheme.surfaceVariant,
    ) {
        Column(Modifier.padding(horizontal = 10.dp, vertical = 8.dp)) {
            Text("$emoji $value", fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
            Text(label, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun BoardPage(repo: Repository, version: Int, date: LocalDate) {
    val board = remember(version, date) { repo.board(date) }
    val editable = repo.canCreate(date)
    val context = LocalContext.current
    var showAdd by remember { mutableStateOf(false) }
    var showAi by remember { mutableStateOf(false) }

    Box(
        Modifier
            .fillMaxSize()
            .padding(bottom = 12.dp)
            .clip(RoundedCornerShape(24.dp))
            .background(BoardColors.wood)
            .padding(5.dp)
            .clip(RoundedCornerShape(20.dp))
            .background(BoardColors.board)
            .padding(12.dp),
    ) {
        if (board == null) {
            Text(
                "За этот день доски нет",
                color = BoardColors.textDim,
                modifier = Modifier.align(Alignment.Center),
            )
        } else {
            val done = board.doneCount
            val total = board.items.size
            Column(Modifier.fillMaxSize()) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        if (total == 0) "Доска пуста" else "Выполнено $done из $total",
                        color = BoardColors.text,
                        fontWeight = FontWeight.Bold,
                        modifier = Modifier.weight(1f),
                    )
                    if (total > 0 && done == total) Text("🎉 Всё сделано!", color = BoardColors.accent)
                }
                LinearProgressIndicator(
                    progress = { if (total == 0) 0f else done.toFloat() / total },
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 8.dp),
                    color = BoardColors.accent,
                    trackColor = Color.White.copy(alpha = 0.2f),
                )
                LazyColumn(
                    modifier = Modifier.weight(1f),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    itemsIndexed(board.items, key = { index, item -> "$index-${item.task.id}" }) { index, item ->
                        BoardTaskRow(
                            item = item,
                            onToggle = { repo.toggle(date, index) },
                            onRemove = { repo.removeTask(date, index) },
                            onSave = {
                                repo.saveTask(item.task)
                                toast(context, "Сохранено в «Мои задания»")
                            },
                        )
                    }
                    if (total == 0) {
                        item {
                            Text(
                                "Нажми «➕ Задание», чтобы добавить что-нибудь, или включи сферы в настройках.",
                                color = BoardColors.textDim,
                                modifier = Modifier.padding(8.dp),
                            )
                        }
                    }
                }
                if (editable) {
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 8.dp),
                    ) {
                        BoardButton("🔄 Обновить", Modifier.weight(1f)) {
                            repo.regenerate(date)
                            toast(context, "Невыполненные задания заменены")
                        }
                        BoardButton("➕ Задание", Modifier.weight(1f)) { showAdd = true }
                        BoardButton("✨ ИИ", Modifier.weight(1f)) { showAi = true }
                    }
                }
            }
        }
    }

    if (showAdd && board != null) {
        AddTaskDialog(
            existing = board.items.map { it.task.id }.toSet(),
            onDismiss = { showAdd = false },
            onAdd = { task ->
                repo.addTask(date, task)
                showAdd = false
            },
        )
    }
    if (showAi) {
        AiBoardDialog(repo = repo, date = date, onDismiss = { showAi = false })
    }
}

@Composable
private fun BoardButton(text: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Box(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .background(Color.White.copy(alpha = 0.14f))
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text, color = BoardColors.text, fontWeight = FontWeight.Medium, fontSize = 13.sp, maxLines = 1)
    }
}

@Composable
private fun BoardTaskRow(item: BoardItem, onToggle: () -> Unit, onRemove: () -> Unit, onSave: () -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    val task = item.task
    Column(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(if (item.done) BoardColors.itemDone else BoardColors.item)
            .clickable { expanded = !expanded }
            .padding(start = 12.dp, top = 8.dp, bottom = 8.dp, end = 4.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(
                    "${task.sphere.emoji} ${task.sphere.shortTitle}",
                    color = sphereColorOnBoard(task.sphere),
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                )
                Text(
                    task.title,
                    color = if (item.done) BoardColors.textDim else BoardColors.text,
                    fontWeight = FontWeight.Bold,
                    textDecoration = if (item.done) TextDecoration.LineThrough else TextDecoration.None,
                )
                Text(
                    "${task.difficulty.stars} · ${task.minutes} мин · ${task.topic}",
                    color = BoardColors.textDim,
                    fontSize = 12.sp,
                )
            }
            Checkbox(
                checked = item.done,
                onCheckedChange = { onToggle() },
                colors = CheckboxDefaults.colors(
                    checkedColor = BoardColors.accent,
                    uncheckedColor = Color.White.copy(alpha = 0.7f),
                    checkmarkColor = BoardColors.board,
                ),
            )
        }
        if (expanded) {
            Text(
                task.description,
                color = BoardColors.text.copy(alpha = 0.92f),
                modifier = Modifier.padding(top = 6.dp, end = 8.dp),
            )
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Источник: ${task.source.title}", color = BoardColors.textDim, fontSize = 12.sp, modifier = Modifier.weight(1f))
                if (task.source != TaskSource.BASE) {
                    TextButton(onClick = onSave) { Text("В «Мои»", color = BoardColors.accent) }
                }
                TextButton(onClick = onRemove) { Text("Убрать", color = BoardColors.accent) }
            }
        } else {
            Text("Нажми, чтобы прочитать задание", color = BoardColors.textDim.copy(alpha = 0.5f), fontSize = 11.sp)
        }
    }
}

@Composable
private fun AddTaskDialog(existing: Set<String>, onDismiss: () -> Unit, onAdd: (PracticeTask) -> Unit) {
    var sphere by remember { mutableStateOf(Sphere.CODE) }
    var level by remember { mutableIntStateOf(1) }
    var fromGenerator by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Добавить задание") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                SphereChips(sphere) { sphere = it }
                DifficultyChips(level, allowAll = false) { level = it }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(selected = !fromGenerator, onClick = { fromGenerator = false }, label = { Text("Из базы") })
                    FilterChip(selected = fromGenerator, onClick = { fromGenerator = true }, label = { Text("🎲 Генератор") })
                }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val difficulty = Difficulty.fromLevel(level)
                val task = if (fromGenerator) {
                    TaskGenerator.generate(sphere, difficulty)
                } else {
                    TaskBase.forSphere(sphere)
                        .filter { it.difficulty == difficulty && it.id !in existing }
                        .randomOrNull() ?: TaskGenerator.generate(sphere, difficulty)
                }
                onAdd(task)
            }) { Text("Добавить случайное") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Отмена") } },
    )
}

@Composable
private fun AiBoardDialog(repo: Repository, date: LocalDate, onDismiss: () -> Unit) {
    val settings = remember { repo.settings() }
    val scope = rememberCoroutineScope()
    var sphere by remember { mutableStateOf(Sphere.CODE) }
    var level by remember { mutableIntStateOf(1) }
    var count by remember { mutableIntStateOf(1) }
    var topic by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    AlertDialog(
        onDismissRequest = { if (!loading) onDismiss() },
        title = { Text("✨ Задания от ИИ") },
        text = {
            if (!settings.aiReady) {
                Text("Чтобы ИИ придумывал задания, открой «Настройки», включи ИИ-наставника и вставь API-ключ Anthropic. Без ИИ работают база и генератор.")
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    SphereChips(sphere) { sphere = it }
                    DifficultyChips(level, allowAll = false) { level = it }
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Сколько:")
                        (1..3).forEach { n ->
                            FilterChip(selected = count == n, onClick = { count = n }, label = { Text("$n") })
                        }
                    }
                    OutlinedTextField(
                        value = topic,
                        onValueChange = { topic = it },
                        label = { Text("Тема (необязательно)") },
                        placeholder = { Text(topicPlaceholder(sphere)) },
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    if (loading) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                            Spacer(Modifier.size(10.dp))
                            Text("ИИ придумывает задания…")
                        }
                    }
                    error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                    Spacer(Modifier.height(2.dp))
                }
            }
        },
        confirmButton = {
            if (settings.aiReady) {
                TextButton(enabled = !loading, onClick = {
                    loading = true
                    error = null
                    scope.launch {
                        generateWithAi(repo, settings, sphere, Difficulty.fromLevel(level), count, topic)
                            .onSuccess { tasks ->
                                tasks.forEach { repo.addTask(date, it) }
                                loading = false
                                onDismiss()
                            }
                            .onFailure {
                                loading = false
                                error = it.message ?: "Неизвестная ошибка"
                            }
                    }
                }) { Text("Придумать") }
            } else {
                TextButton(onClick = onDismiss) { Text("Понятно") }
            }
        },
        dismissButton = {
            if (settings.aiReady) TextButton(enabled = !loading, onClick = onDismiss) { Text("Отмена") }
        },
    )
}
