// SPDX-License-Identifier: GPL-3.0-or-later
package jp.aonesenbongi.meltype.conversionfixture;

import android.app.Activity;
import android.os.Bundle;
import android.os.Handler;
import android.view.inputmethod.BaseInputConnection;
import android.widget.Button;
import android.widget.TextView;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.LinearLayout;

public class ConversionActivity extends Activity {
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(0, (int)(60 * getResources().getDisplayMetrics().density), 0, 0);
        EditText editor = new EditText(this);
        editor.setContentDescription("fixture-editor");
        editor.setInputType(1);
        if (getIntent().getBooleanExtra("password", false)) editor.setInputType(129);
        editor.setImeOptions(EditorInfo.IME_ACTION_DONE | (getIntent().getBooleanExtra("personal", false)
            ? 0 : EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING));
        root.addView(editor);
        Button select = new Button(this);
        select.setText("Select first clause");
        select.setContentDescription("fixture-select-first");
        select.setFocusable(false);
        select.setOnClickListener(v -> editor.setSelection(getIntent().getIntExtra("select", 1)));
        root.addView(select);
        Button copy = new Button(this);
        copy.setText("Copy test text");
        copy.setContentDescription("fixture-copy");
        copy.setFocusable(false);
        copy.setOnClickListener(v -> {
            android.content.ClipData clip = android.content.ClipData.newPlainText("test",
                getIntent().getStringExtra("clipboard") == null ? "clipboard-test" : getIntent().getStringExtra("clipboard"));
            if (getIntent().getBooleanExtra("sensitive", false)) {
                android.os.PersistableBundle extras = new android.os.PersistableBundle();
                extras.putBoolean("android.content.extra.IS_SENSITIVE", true);
                clip.getDescription().setExtras(extras);
            }
            ((android.content.ClipboardManager)getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(clip);
            ((android.view.inputmethod.InputMethodManager)getSystemService(INPUT_METHOD_SERVICE))
                .hideSoftInputFromWindow(editor.getWindowToken(), 0);
        });
        root.addView(copy);
        TextView composing = new TextView(this);
        composing.setContentDescription("fixture-composing");
        root.addView(composing);
        TextView selection = new TextView(this);
        selection.setContentDescription("fixture-selection"); root.addView(selection);
        Handler handler = new Handler();
        handler.post(new Runnable() {
            public void run() {
                selection.setText(editor.getSelectionStart()+","+editor.getSelectionEnd());
                composing.setText(BaseInputConnection.getComposingSpanStart(editor.getText()) + "," +
                    BaseInputConnection.getComposingSpanEnd(editor.getText()));
                handler.postDelayed(this, 100);
            }
        });
        setContentView(root);
        editor.requestFocus();
    }
}
