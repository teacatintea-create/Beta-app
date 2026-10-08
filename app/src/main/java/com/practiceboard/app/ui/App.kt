package com.practiceboard.app.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.sp
import com.practiceboard.app.data.Repository
import com.practiceboard.app.data.Sphere
import java.time.LocalDate

private enum class Section(val label: String, val emoji: String) {
    BOARDS("Доски", "📋"),
    CODE("Код", "💻"),
    MODEL("3D", "🧊"),
    ART("2D-арт", "🎨"),
    SETTINGS("Настройки", "⚙️"),
}

@Composable
fun PracticeApp(repo: Repository, openDate: LocalDate?, openRequest: Int) {
    var section by rememberSaveable { mutableStateOf(Section.BOARDS) }
    val version by repo.changes.collectAsState()

    // Нажатие на виджет всегда ведёт к доскам.
    LaunchedEffect(openRequest) {
        if (openRequest > 0) section = Section.BOARDS
    }

    Scaffold(
        bottomBar = {
            NavigationBar {
                Section.entries.forEach { s ->
                    NavigationBarItem(
                        selected = section == s,
                        onClick = { section = s },
                        icon = { Text(s.emoji, fontSize = 20.sp) },
                        label = { Text(s.label, maxLines = 1) },
                    )
                }
            }
        },
    ) { padding ->
        Box(
            Modifier
                .fillMaxSize()
                .padding(padding),
        ) {
            when (section) {
                Section.BOARDS -> BoardsScreen(repo, version, openDate, openRequest)
                Section.CODE -> SphereScreen(repo, version, Sphere.CODE)
                Section.MODEL -> SphereScreen(repo, version, Sphere.MODEL3D)
                Section.ART -> SphereScreen(repo, version, Sphere.ART2D)
                Section.SETTINGS -> SettingsScreen(repo, version)
            }
        }
    }
}
