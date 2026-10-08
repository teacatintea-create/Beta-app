package com.practiceboard.app.widget

import android.appwidget.AppWidgetManager
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import android.widget.RemoteViewsService
import com.practiceboard.app.R
import com.practiceboard.app.data.BoardItem
import com.practiceboard.app.data.Repository
import com.practiceboard.app.data.Sphere
import java.time.LocalDate

/** Какой день сейчас показывает каждый экземпляр виджета. */
object WidgetState {
    private fun prefs(context: Context) = context.getSharedPreferences("widget_state", Context.MODE_PRIVATE)

    fun date(context: Context, id: Int): LocalDate =
        prefs(context).getString("date_$id", null)?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
            ?: LocalDate.now()

    fun setDate(context: Context, id: Int, date: LocalDate) {
        prefs(context).edit().putString("date_$id", date.toString()).apply()
    }

    fun clear(context: Context, id: Int) {
        prefs(context).edit().remove("date_$id").apply()
    }

    fun resetAll(context: Context) {
        prefs(context).edit().clear().apply()
    }
}

class BoardWidgetService : RemoteViewsService() {
    override fun onGetViewFactory(intent: Intent): RemoteViewsFactory =
        BoardItemsFactory(applicationContext, intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID))
}

private class BoardItemsFactory(private val context: Context, private val widgetId: Int) : RemoteViewsService.RemoteViewsFactory {

    private var date: LocalDate = LocalDate.now()
    private var items: List<BoardItem> = emptyList()

    override fun onCreate() = Unit

    override fun onDataSetChanged() {
        date = WidgetState.date(context, widgetId)
        items = Repository.get(context).board(date)?.items.orEmpty()
    }

    override fun onDestroy() = Unit

    override fun getCount(): Int = items.size

    override fun getViewAt(position: Int): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_item)
        val item = items.getOrNull(position) ?: return views
        val task = item.task
        views.setTextViewText(R.id.item_sphere, "${task.sphere.emoji} ${task.sphere.shortTitle}")
        views.setTextColor(R.id.item_sphere, sphereColor(task.sphere))
        views.setTextViewText(R.id.item_title, task.title)
        views.setTextViewText(R.id.item_meta, "${task.difficulty.stars} · ${task.minutes} мин · ${task.topic}")
        views.setTextViewText(R.id.item_check, if (item.done) "✅" else "⬜")
        views.setTextColor(R.id.item_title, if (item.done) 0x99FFFFFF.toInt() else 0xFFFFFFFF.toInt())
        views.setInt(R.id.item_root, "setBackgroundResource", if (item.done) R.drawable.widget_item_done_bg else R.drawable.widget_item_bg)

        val fillIn = Intent()
            .putExtra(BoardWidgetProvider.EXTRA_DATE, date.toString())
            .putExtra(BoardWidgetProvider.EXTRA_INDEX, position)
            .putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, widgetId)
        views.setOnClickFillInIntent(R.id.item_root, fillIn)
        return views
    }

    override fun getLoadingView(): RemoteViews? = null

    override fun getViewTypeCount(): Int = 1

    override fun getItemId(position: Int): Long = position.toLong()

    override fun hasStableIds(): Boolean = false

    private fun sphereColor(sphere: Sphere): Int = when (sphere) {
        Sphere.CODE -> 0xFF7FD3FF.toInt()
        Sphere.MODEL3D -> 0xFFFFC27A.toInt()
        Sphere.ART2D -> 0xFFFF8FB8.toInt()
    }
}
