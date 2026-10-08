@file:OptIn(ExperimentalMaterial3Api::class)

package com.practiceboard.app.ui

import android.content.Context
import android.widget.Toast
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.practiceboard.app.ai.ClaudeTaskGenerator
import com.practiceboard.app.data.AppSettings
import com.practiceboard.app.data.Difficulty
import com.practiceboard.app.data.PracticeTask
import com.practiceboard.app.data.Repository
import com.practiceboard.app.data.Sphere
import com.practiceboard.app.data.TaskSource
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

fun toast(context: Context, text: String) {
    Toast.makeText(context, text, Toast.LENGTH_SHORT).show()
}

/** Карточка задания для списков и генератора. */
@Composable
fun TaskCard(task: PracticeTask, actions: @Composable RowScope.() -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clickable { expanded = !expanded },
    ) {
        Column(Modifier.padding(start = 14.dp, end = 14.dp, top = 12.dp, bottom = 4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(task.difficulty.stars, color = sphereColor(task.sphere), style = MaterialTheme.typography.labelLarge)
                Text(
                    "  ${task.topic}",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                val source = if (task.source == TaskSource.BASE) "" else " · ${task.source.title}"
                Text(
                    "${task.minutes} мин$source",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Text(
                task.title,
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(top = 4.dp),
            )
            Text(
                task.description,
                style = MaterialTheme.typography.bodyMedium,
                maxLines = if (expanded) Int.MAX_VALUE else 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 2.dp),
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.End,
                verticalAlignment = Alignment.CenterVertically,
                content = actions,
            )
        }
    }
}

@Composable
fun DifficultyChips(selected: Int, allowAll: Boolean, onSelect: (Int) -> Unit) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        if (allowAll) {
            item {
                FilterChip(selected = selected == 0, onClick = { onSelect(0) }, label = { Text("Все") })
            }
        }
        items(Difficulty.entries) { d ->
            FilterChip(
                selected = selected == d.level,
                onClick = { onSelect(d.level) },
                label = { Text("${d.stars} ${d.title}") },
            )
        }
    }
}

@Composable
fun SphereChips(selected: Sphere, onSelect: (Sphere) -> Unit) {
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(Sphere.entries) { s ->
            FilterChip(
                selected = selected == s,
                onClick = { onSelect(s) },
                label = { Text("${s.emoji} ${s.shortTitle}") },
            )
        }
    }
}

/** Запрос к ИИ в фоне. Результат сразу сохраняется в «Мои задания», чтобы пополнять базу. */
suspend fun generateWithAi(
    repo: Repository,
    settings: AppSettings,
    sphere: Sphere,
    difficulty: Difficulty,
    count: Int,
    topic: String,
): Result<List<PracticeTask>> = withContext(Dispatchers.IO) {
    runCatching {
        val tasks = ClaudeTaskGenerator(settings.apiKey, settings.aiModel)
            .generate(sphere, difficulty, count, topic, repo.recentTitles(sphere))
        if (tasks.isEmpty()) error("ИИ не вернул ни одного задания. Попробуй ещё раз.")
        tasks
    }
}.onSuccess { tasks -> tasks.forEach { repo.saveTask(it) } }

fun topicPlaceholder(sphere: Sphere): String = when (sphere) {
    Sphere.CODE -> "например: корутины, игры, работа со строками"
    Sphere.MODEL3D -> "например: персонажи, Geometry Nodes, оружие"
    Sphere.ART2D -> "например: руки, пейзажи, свет на закате"
}
