package com.practiceboard.app.data

import android.content.Context
import android.content.SharedPreferences
import com.practiceboard.app.widget.BoardWidgetProvider
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDate

/**
 * Единое хранилище для приложения и виджета: доски по дням, настройки,
 * сохранённые задания (от генератора и ИИ) и изученные темы.
 */
class Repository private constructor(private val context: Context) {

    private val prefs: SharedPreferences = context.getSharedPreferences("practice_board", Context.MODE_PRIVATE)

    private val _changes = MutableStateFlow(0)

    /** Увеличивается при каждом изменении данных — экраны перечитывают данные. */
    val changes: StateFlow<Int> = _changes

    // ------------------------------------------------------------------ Настройки

    @Synchronized
    fun settings(): AppSettings = prefs.getString(KEY_SETTINGS, null)
        ?.let { runCatching { AppSettings.fromJson(JSONObject(it)) }.getOrNull() }
        ?: AppSettings()

    /** Сохраняет настройки. Доски на сегодня и вперёд без выполненных заданий пересобираются. */
    @Synchronized
    fun saveSettings(settings: AppSettings) {
        val boardChanged = settings().boardSignature() != settings.boardSignature()
        prefs.edit().putString(KEY_SETTINGS, settings.toJson().toString()).apply()
        if (boardChanged) {
            val today = LocalDate.now()
            for (offset in 0..MAX_FUTURE_DAYS) {
                val date = today.plusDays(offset.toLong())
                val board = loadBoard(date) ?: continue
                if (board.items.none { it.done }) saveBoard(createBoard(date, board.salt + 1))
            }
        }
        notifyChanged()
    }

    // ------------------------------------------------------------------ Доски

    @Synchronized
    fun board(date: LocalDate): DayBoard? {
        loadBoard(date)?.let { return it }
        if (!canCreate(date)) return null
        return createBoard(date, 0).also { saveBoard(it) }
    }

    fun canCreate(date: LocalDate): Boolean {
        val today = LocalDate.now()
        return !date.isBefore(today) && !date.isAfter(today.plusDays(MAX_FUTURE_DAYS.toLong()))
    }

    @Synchronized
    fun toggle(date: LocalDate, index: Int) {
        val board = board(date) ?: return
        if (index !in board.items.indices) return
        val items = board.items.toMutableList()
        items[index] = items[index].copy(done = !items[index].done)
        saveBoard(board.copy(items = items))
        notifyChanged()
    }

    /** Пересоздаёт доску. Выполненные задания остаются на месте. */
    @Synchronized
    fun regenerate(date: LocalDate) {
        val old = board(date) ?: return
        val fresh = createBoard(date, old.salt + 1)
        val kept = old.items.filter { it.done }
        val keptIds = kept.map { it.task.id }.toSet()
        val replacement = fresh.items.filter { it.task.id !in keptIds }.take((old.items.size - kept.size).coerceAtLeast(0))
        saveBoard(fresh.copy(items = kept + replacement))
        notifyChanged()
    }

    @Synchronized
    fun addTask(date: LocalDate, task: PracticeTask) {
        val board = board(date) ?: DayBoard(date, emptyList())
        if (board.items.any { it.task.id == task.id }) return
        saveBoard(board.copy(items = board.items + BoardItem(task, false)))
        notifyChanged()
    }

    @Synchronized
    fun removeTask(date: LocalDate, index: Int) {
        val board = board(date) ?: return
        if (index !in board.items.indices) return
        saveBoard(board.copy(items = board.items.filterIndexed { i, _ -> i != index }))
        notifyChanged()
    }

    /** Названия заданий последних дней — чтобы ИИ не повторялся. */
    @Synchronized
    fun recentTitles(sphere: Sphere, days: Int = 14): List<String> {
        val today = LocalDate.now()
        return (0..days).flatMap { loadBoard(today.minusDays(it.toLong()))?.items.orEmpty() }
            .map { it.task }.filter { it.sphere == sphere }.map { it.title }.distinct()
    }

    private fun createBoard(date: LocalDate, salt: Int): DayBoard {
        val recent = (1..RECENT_DAYS).flatMap { loadBoard(date.minusDays(it.toLong()))?.items.orEmpty() }
            .map { it.task.id }.toSet()
        return DayBoard(date, BoardBuilder.build(date, settings(), recent, salt), salt)
    }

    private fun loadBoard(date: LocalDate): DayBoard? = prefs.getString(boardKey(date), null)
        ?.let { runCatching { DayBoard.fromJson(JSONObject(it)) }.getOrNull() }

    private fun saveBoard(board: DayBoard) {
        val editor = prefs.edit().putString(boardKey(board.date), board.toJson().toString())
        // Подчищаем очень старые доски, чтобы хранилище не разрасталось.
        val limit = LocalDate.now().minusDays(KEEP_DAYS.toLong())
        prefs.all.keys.filter { it.startsWith(BOARD_PREFIX) }.forEach { key ->
            val d = runCatching { LocalDate.parse(key.removePrefix(BOARD_PREFIX)) }.getOrNull()
            if (d != null && d.isBefore(limit)) editor.remove(key)
        }
        editor.apply()
    }

    private fun boardKey(date: LocalDate) = BOARD_PREFIX + date

    // ------------------------------------------------------------------ Статистика

    @Synchronized
    fun stats(): Stats {
        val boards = prefs.all.keys.filter { it.startsWith(BOARD_PREFIX) }
            .mapNotNull { runCatching { LocalDate.parse(it.removePrefix(BOARD_PREFIX)) }.getOrNull() }
            .mapNotNull { loadBoard(it) }
        val doneItems = boards.flatMap { b -> b.items.filter { it.done } }
        val activeDates = boards.filter { b -> b.items.any { it.done } }.map { it.date }.toSet()
        var day = LocalDate.now()
        if (day !in activeDates) day = day.minusDays(1)
        var streak = 0
        while (day in activeDates) {
            streak++
            day = day.minusDays(1)
        }
        return Stats(
            streak = streak,
            totalDone = doneItems.size,
            doneBySphere = Sphere.entries.associateWith { s -> doneItems.count { it.task.sphere == s } },
            activeDays = activeDates.size,
        )
    }

    // ------------------------------------------------------------------ Сохранённые задания

    @Synchronized
    fun savedTasks(sphere: Sphere? = null): List<PracticeTask> {
        val arr = prefs.getString(KEY_SAVED, null)?.let { runCatching { JSONArray(it) }.getOrNull() } ?: JSONArray()
        return (0 until arr.length()).map { PracticeTask.fromJson(arr.getJSONObject(it)) }
            .filter { sphere == null || it.sphere == sphere }
    }

    @Synchronized
    fun isSaved(task: PracticeTask): Boolean = savedTasks().any { it.id == task.id }

    @Synchronized
    fun saveTask(task: PracticeTask) {
        val all = savedTasks()
        if (all.any { it.id == task.id }) return
        writeSaved(all + task)
        notifyChanged()
    }

    @Synchronized
    fun deleteSavedTask(task: PracticeTask) {
        writeSaved(savedTasks().filter { it.id != task.id })
        notifyChanged()
    }

    private fun writeSaved(list: List<PracticeTask>) {
        prefs.edit().putString(KEY_SAVED, JSONArray().apply { list.forEach { put(it.toJson()) } }.toString()).apply()
    }

    // ------------------------------------------------------------------ Обучение

    @Synchronized
    fun learnedTopics(): Set<String> = prefs.getStringSet(KEY_LEARNED, emptySet()).orEmpty().toSet()

    @Synchronized
    fun setLearned(topicId: String, learned: Boolean) {
        val set = learnedTopics().toMutableSet()
        if (learned) set += topicId else set -= topicId
        prefs.edit().putStringSet(KEY_LEARNED, set).apply()
        notifyChanged()
    }

    // ------------------------------------------------------------------ Сброс

    @Synchronized
    fun resetProgress() {
        val editor = prefs.edit()
        prefs.all.keys.filter { it.startsWith(BOARD_PREFIX) }.forEach { editor.remove(it) }
        editor.remove(KEY_LEARNED).apply()
        notifyChanged()
    }

    private fun notifyChanged() {
        _changes.value = _changes.value + 1
        BoardWidgetProvider.updateAll(context)
    }

    companion object {
        const val MAX_FUTURE_DAYS = 2
        const val MAX_PAST_DAYS = 13
        private const val RECENT_DAYS = 21
        private const val KEEP_DAYS = 90
        private const val BOARD_PREFIX = "board_"
        private const val KEY_SETTINGS = "settings"
        private const val KEY_SAVED = "saved_tasks"
        private const val KEY_LEARNED = "learned_topics"

        @Volatile
        private var instance: Repository? = null

        fun get(context: Context): Repository =
            instance ?: synchronized(this) {
                instance ?: Repository(context.applicationContext).also { instance = it }
            }
    }
}
