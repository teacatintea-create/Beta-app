package com.practiceboard.app.widget

import android.app.AlarmManager
import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.view.View
import android.widget.RemoteViews
import com.practiceboard.app.MainActivity
import com.practiceboard.app.R
import com.practiceboard.app.data.Repository
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * Виджет «Доска дня»: каждая доска — отдельный день, стрелками можно листать дни.
 * Нажатие на задание отмечает его выполненным (то же, что и в приложении).
 */
class BoardWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(context: Context, manager: AppWidgetManager, appWidgetIds: IntArray) {
        appWidgetIds.forEach { updateWidget(context, manager, it) }
        scheduleMidnightUpdate(context)
    }

    override fun onEnabled(context: Context) {
        scheduleMidnightUpdate(context)
    }

    override fun onDeleted(context: Context, appWidgetIds: IntArray) {
        appWidgetIds.forEach { WidgetState.clear(context, it) }
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        val id = intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID)
        when (intent.action) {
            ACTION_PREV -> shiftDay(context, id, -1)
            ACTION_NEXT -> shiftDay(context, id, 1)
            ACTION_TODAY -> {
                WidgetState.setDate(context, id, LocalDate.now())
                updateWidget(context, AppWidgetManager.getInstance(context), id)
            }
            ACTION_TOGGLE -> {
                val date = intent.getStringExtra(EXTRA_DATE)?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
                val index = intent.getIntExtra(EXTRA_INDEX, -1)
                if (date != null && index >= 0) Repository.get(context).toggle(date, index) // обновит все виджеты
            }
            ACTION_MIDNIGHT, Intent.ACTION_TIME_CHANGED, Intent.ACTION_TIMEZONE_CHANGED -> {
                WidgetState.resetAll(context)
                updateAll(context)
                scheduleMidnightUpdate(context)
            }
        }
    }

    private fun shiftDay(context: Context, id: Int, delta: Long) {
        if (id == AppWidgetManager.INVALID_APPWIDGET_ID) return
        val today = LocalDate.now()
        val min = today.minusDays(Repository.MAX_PAST_DAYS.toLong())
        val max = today.plusDays(Repository.MAX_FUTURE_DAYS.toLong())
        var date = WidgetState.date(context, id).plusDays(delta)
        if (date.isBefore(min)) date = min
        if (date.isAfter(max)) date = max
        WidgetState.setDate(context, id, date)
        updateWidget(context, AppWidgetManager.getInstance(context), id)
    }

    companion object {
        const val ACTION_PREV = "com.practiceboard.app.widget.PREV"
        const val ACTION_NEXT = "com.practiceboard.app.widget.NEXT"
        const val ACTION_TODAY = "com.practiceboard.app.widget.TODAY"
        const val ACTION_TOGGLE = "com.practiceboard.app.widget.TOGGLE"
        const val ACTION_MIDNIGHT = "com.practiceboard.app.widget.MIDNIGHT"
        const val EXTRA_DATE = "date"
        const val EXTRA_INDEX = "index"

        private val ru = Locale("ru")
        private val dateFormat = DateTimeFormatter.ofPattern("d MMMM, EEEE", ru)

        fun updateAll(context: Context) {
            val manager = AppWidgetManager.getInstance(context) ?: return
            val ids = manager.getAppWidgetIds(ComponentName(context, BoardWidgetProvider::class.java))
            ids.forEach { updateWidget(context, manager, it) }
        }

        fun dayTitle(date: LocalDate, today: LocalDate = LocalDate.now()): String = when (date) {
            today -> "Сегодня"
            today.plusDays(1) -> "Завтра"
            today.plusDays(2) -> "Послезавтра"
            today.minusDays(1) -> "Вчера"
            else -> date.format(DateTimeFormatter.ofPattern("EEEE", ru)).replaceFirstChar { it.uppercase() }
        }

        fun dateLine(date: LocalDate): String = date.format(dateFormat)

        fun updateWidget(context: Context, manager: AppWidgetManager, id: Int) {
            val repo = Repository.get(context)
            val date = WidgetState.date(context, id)
            val board = repo.board(date)
            val total = board?.items?.size ?: 0
            val done = board?.doneCount ?: 0

            val views = RemoteViews(context.packageName, R.layout.widget_board)
            views.setTextViewText(R.id.widget_title, dayTitle(date))
            views.setTextViewText(R.id.widget_subtitle, "${dateLine(date)} · $done/$total")
            views.setProgressBar(R.id.widget_progress, total.coerceAtLeast(1), done, false)
            views.setViewVisibility(R.id.widget_today, if (date == LocalDate.now()) View.GONE else View.VISIBLE)
            views.setTextViewText(
                R.id.widget_empty,
                if (repo.canCreate(date)) "На этот день заданий нет.\nДобавь их в приложении." else "За этот день доски нет",
            )

            // Список заданий
            val serviceIntent = Intent(context, BoardWidgetService::class.java)
                .putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, id)
            serviceIntent.data = Uri.parse(serviceIntent.toUri(Intent.URI_INTENT_SCHEME))
            views.setRemoteAdapter(R.id.widget_list, serviceIntent)
            views.setEmptyView(R.id.widget_list, R.id.widget_empty)

            val toggleTemplate = PendingIntent.getBroadcast(
                context, id,
                Intent(context, BoardWidgetProvider::class.java).setAction(ACTION_TOGGLE),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE,
            )
            views.setPendingIntentTemplate(R.id.widget_list, toggleTemplate)

            // Кнопки
            views.setOnClickPendingIntent(R.id.widget_prev, actionIntent(context, id, ACTION_PREV, 1))
            views.setOnClickPendingIntent(R.id.widget_next, actionIntent(context, id, ACTION_NEXT, 2))
            views.setOnClickPendingIntent(R.id.widget_today, actionIntent(context, id, ACTION_TODAY, 3))
            val open = Intent(context, MainActivity::class.java)
                .putExtra(MainActivity.EXTRA_OPEN_DATE, date.toString())
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            val openPending = PendingIntent.getActivity(
                context, id * 10 + 4, open,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
            views.setOnClickPendingIntent(R.id.widget_open, openPending)
            views.setOnClickPendingIntent(R.id.widget_header, openPending)

            manager.updateAppWidget(id, views)
            @Suppress("DEPRECATION")
            manager.notifyAppWidgetViewDataChanged(id, R.id.widget_list)
        }

        private fun actionIntent(context: Context, id: Int, action: String, code: Int): PendingIntent =
            PendingIntent.getBroadcast(
                context, id * 10 + code,
                Intent(context, BoardWidgetProvider::class.java)
                    .setAction(action)
                    .putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, id),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )

        /** Будильник на полночь: в новый день виджет показывает новую доску. */
        fun scheduleMidnightUpdate(context: Context) {
            val alarm = context.getSystemService(AlarmManager::class.java) ?: return
            val pending = PendingIntent.getBroadcast(
                context, 0,
                Intent(context, BoardWidgetProvider::class.java).setAction(ACTION_MIDNIGHT),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
            val nextMidnight = LocalDate.now().plusDays(1).atStartOfDay(ZoneId.systemDefault())
                .toInstant().toEpochMilli() + 5_000
            alarm.set(AlarmManager.RTC, nextMidnight, pending)
        }
    }
}
