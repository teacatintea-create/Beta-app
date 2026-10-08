package com.practiceboard.app.data

import org.json.JSONArray
import org.json.JSONObject
import java.time.DayOfWeek
import java.time.LocalDate

enum class Sphere(val id: String, val title: String, val shortTitle: String, val emoji: String) {
    CODE("code", "Программирование", "Код", "💻"),
    MODEL3D("3d", "3D-моделирование", "3D", "🧊"),
    ART2D("2d", "2D-арт", "2D", "🎨");

    companion object {
        fun fromId(id: String?): Sphere = entries.firstOrNull { it.id == id } ?: CODE
    }
}

enum class Difficulty(val level: Int, val title: String, val stars: String) {
    EASY(1, "Новичок", "★☆☆"),
    MEDIUM(2, "Средний", "★★☆"),
    HARD(3, "Продвинутый", "★★★");

    companion object {
        fun fromLevel(level: Int): Difficulty = entries.firstOrNull { it.level == level } ?: EASY
    }
}

enum class TaskSource(val id: String, val title: String) {
    BASE("base", "База"),
    GENERATOR("gen", "Генератор"),
    AI("ai", "ИИ");

    companion object {
        fun fromId(id: String?): TaskSource = entries.firstOrNull { it.id == id } ?: BASE
    }
}

data class PracticeTask(
    val id: String,
    val sphere: Sphere,
    val title: String,
    val description: String,
    val difficulty: Difficulty,
    val topic: String,
    val minutes: Int,
    val source: TaskSource = TaskSource.BASE,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("id", id)
        .put("sphere", sphere.id)
        .put("title", title)
        .put("description", description)
        .put("difficulty", difficulty.level)
        .put("topic", topic)
        .put("minutes", minutes)
        .put("source", source.id)

    companion object {
        fun fromJson(o: JSONObject) = PracticeTask(
            id = o.getString("id"),
            sphere = Sphere.fromId(o.optString("sphere")),
            title = o.optString("title"),
            description = o.optString("description"),
            difficulty = Difficulty.fromLevel(o.optInt("difficulty", 1)),
            topic = o.optString("topic"),
            minutes = o.optInt("minutes", 30),
            source = TaskSource.fromId(o.optString("source")),
        )
    }
}

data class BoardItem(val task: PracticeTask, val done: Boolean) {
    fun toJson(): JSONObject = JSONObject().put("task", task.toJson()).put("done", done)

    companion object {
        fun fromJson(o: JSONObject) = BoardItem(PracticeTask.fromJson(o.getJSONObject("task")), o.optBoolean("done"))
    }
}

/** Доска заданий на один день. */
data class DayBoard(val date: LocalDate, val items: List<BoardItem>, val salt: Int = 0) {
    val doneCount: Int get() = items.count { it.done }

    fun toJson(): JSONObject = JSONObject()
        .put("date", date.toString())
        .put("salt", salt)
        .put("items", JSONArray().apply { items.forEach { put(it.toJson()) } })

    companion object {
        fun fromJson(o: JSONObject): DayBoard {
            val arr = o.optJSONArray("items") ?: JSONArray()
            return DayBoard(
                date = LocalDate.parse(o.getString("date")),
                items = (0 until arr.length()).map { BoardItem.fromJson(arr.getJSONObject(it)) },
                salt = o.optInt("salt"),
            )
        }
    }
}

enum class DifficultyMode(val id: String, val title: String) {
    EASY("easy", "Новичок"),
    MEDIUM("medium", "Средний"),
    HARD("hard", "Продвинутый"),
    MIXED("mixed", "Смешанный"),
    WEEKLY("weekly", "По дням недели");

    /** Какие уровни сложности допустимы в этот день. */
    fun levelsFor(date: LocalDate): List<Int> = when (this) {
        EASY -> listOf(1)
        MEDIUM -> listOf(2)
        HARD -> listOf(3)
        MIXED -> listOf(1, 2, 3)
        // Пн–Вт легко, Ср–Пт средне, выходные — время для больших задач.
        WEEKLY -> when (date.dayOfWeek) {
            DayOfWeek.MONDAY, DayOfWeek.TUESDAY -> listOf(1)
            DayOfWeek.SATURDAY, DayOfWeek.SUNDAY -> listOf(3)
            else -> listOf(2)
        }
    }

    companion object {
        fun fromId(id: String?): DifficultyMode = entries.firstOrNull { it.id == id } ?: MIXED
    }
}

data class SphereSettings(val count: Int = 2, val mode: DifficultyMode = DifficultyMode.MIXED)

data class AppSettings(
    val spheres: Map<Sphere, SphereSettings> = Sphere.entries.associateWith { SphereSettings() },
    /** Процент заданий доски, которые придумывает процедурный генератор (остальные — из базы). */
    val generatorPercent: Int = 30,
    val aiEnabled: Boolean = false,
    val apiKey: String = "",
    val aiModel: String = DEFAULT_AI_MODEL,
) {
    fun sphere(s: Sphere): SphereSettings = spheres[s] ?: SphereSettings()

    val aiReady: Boolean get() = aiEnabled && apiKey.isNotBlank()

    /** Настройки, от которых зависит состав доски (без ключа ИИ). */
    fun boardSignature(): String =
        Sphere.entries.joinToString(";") { "${it.id}:${sphere(it).count}:${sphere(it).mode.id}" } + ";g$generatorPercent"

    fun toJson(): JSONObject {
        val sp = JSONObject()
        spheres.forEach { (k, v) -> sp.put(k.id, JSONObject().put("count", v.count).put("mode", v.mode.id)) }
        return JSONObject()
            .put("spheres", sp)
            .put("generatorPercent", generatorPercent)
            .put("aiEnabled", aiEnabled)
            .put("apiKey", apiKey)
            .put("aiModel", aiModel)
    }

    companion object {
        const val DEFAULT_AI_MODEL = "claude-opus-5-5"

        fun fromJson(o: JSONObject): AppSettings {
            val sp = o.optJSONObject("spheres") ?: JSONObject()
            val map = Sphere.entries.associateWith { s ->
                val so = sp.optJSONObject(s.id)
                if (so == null) SphereSettings()
                else SphereSettings(so.optInt("count", 2).coerceIn(0, 5), DifficultyMode.fromId(so.optString("mode")))
            }
            return AppSettings(
                spheres = map,
                generatorPercent = o.optInt("generatorPercent", 30).coerceIn(0, 100),
                aiEnabled = o.optBoolean("aiEnabled"),
                apiKey = o.optString("apiKey"),
                aiModel = o.optString("aiModel", DEFAULT_AI_MODEL).ifBlank { DEFAULT_AI_MODEL },
            )
        }
    }
}

data class Stats(val streak: Int, val totalDone: Int, val doneBySphere: Map<Sphere, Int>, val activeDays: Int)
