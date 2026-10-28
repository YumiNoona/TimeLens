package `in`.venusapp.timelens

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import `in`.venusapp.timelens.platform.BackgroundTasks
import `in`.venusapp.timelens.ui.TimeLensApp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        BackgroundTasks.setup(this)
        setContent { TimeLensApp() }
    }
}
