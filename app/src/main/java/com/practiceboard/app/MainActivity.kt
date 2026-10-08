package com.practiceboard.app

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import com.practiceboard.app.data.Repository
import com.practiceboard.app.ui.PracticeApp
import com.practiceboard.app.ui.PracticeTheme
import com.practiceboard.app.widget.BoardWidgetProvider
import java.time.LocalDate

class MainActivity : ComponentActivity() {

    private val openDate = mutableStateOf<LocalDate?>(null)
    private val openRequest = mutableIntStateOf(0)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        handleIntent(intent)
        val repo = Repository.get(this)
        setContent {
            PracticeTheme {
                PracticeApp(repo, openDate.value, openRequest.intValue)
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIntent(intent)
    }

    override fun onStop() {
        super.onStop()
        // На случай, если наступил новый день, пока приложение было открыто.
        BoardWidgetProvider.updateAll(this)
    }

    private fun handleIntent(intent: Intent?) {
        val date = intent?.getStringExtra(EXTRA_OPEN_DATE) ?: return
        openDate.value = runCatching { LocalDate.parse(date) }.getOrNull()
        openRequest.intValue += 1
    }

    companion object {
        const val EXTRA_OPEN_DATE = "open_date"
    }
}
