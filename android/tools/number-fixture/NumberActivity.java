// SPDX-License-Identifier: GPL-3.0-or-later
package jp.aonesenbongi.meltype.numberfixture;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.widget.Button;
import android.os.Bundle;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.LinearLayout;

public class NumberActivity extends Activity {
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(0, (int)(60 * getResources().getDisplayMetrics().density), 0, 0);
        EditText number = new EditText(this);
        number.setContentDescription("number-editor");
        number.setInputType(getIntent().getIntExtra("type", 2));
        number.setImeOptions(EditorInfo.IME_ACTION_NEXT);
        root.addView(number);
        EditText text = new EditText(this);
        text.setContentDescription("text-editor");
        text.setInputType(1);
        root.addView(text);
        Button copy = new Button(this);
        copy.setText("COPY NUMERIC TEST"); copy.setContentDescription("number-copy");
        copy.setOnClickListener(v -> {
            ((ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("test", "12345"));
            ((android.view.inputmethod.InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(number.getWindowToken(), 0);
        }); root.addView(copy);
        setContentView(root);
        number.requestFocus();
    }
}
