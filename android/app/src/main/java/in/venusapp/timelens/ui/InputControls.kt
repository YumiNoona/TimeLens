package `in`.venusapp.timelens.ui

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization

/** Dismiss typing without treating Done/Search as a save or changing the selected page. */
@Composable
internal fun dismissKeyboardActions(): KeyboardActions {
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    return KeyboardActions(
        onDone = {
            focus.clearFocus()
            keyboard?.hide()
        },
        onSearch = {
            focus.clearFocus()
            keyboard?.hide()
        },
    )
}

@Composable
internal fun SearchInput(
    value: String,
    onValueChange: (String) -> Unit,
    label: String,
    clearDescription: String = "Clear search",
    requestFocus: Boolean = false,
    onFocusRequested: () -> Unit = {},
) {
    val requester = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    LaunchedEffect(requestFocus) {
        if (requestFocus) {
            requester.requestFocus()
            keyboard?.show()
            onFocusRequested()
        }
    }
    OutlinedTextField(
        value,
        onValueChange,
        label = { Text(label) },
        modifier = Modifier.fillMaxWidth().focusRequester(requester),
        singleLine = true,
        keyboardOptions =
            KeyboardOptions(
                capitalization = KeyboardCapitalization.None,
                autoCorrectEnabled = false,
                imeAction = ImeAction.Search,
            ),
        keyboardActions = dismissKeyboardActions(),
        leadingIcon = { Icon(Icons.Outlined.Search, null) },
        trailingIcon = {
            if (value.isNotEmpty())
                IconButton(onClick = { onValueChange("") }) {
                    Icon(Icons.Outlined.Close, clearDescription)
                }
        },
    )
}
