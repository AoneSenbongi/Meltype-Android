// SPDX-License-Identifier: GPL-3.0-or-later
package jp.aonesenbongi.meltype.sendfixture;

import android.app.Activity;
import android.os.Bundle;
import android.text.InputType;
import android.view.inputmethod.BaseInputConnection;
import android.view.inputmethod.EditorInfo;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

public class SendActivity extends Activity {
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(0, (int)(60 * getResources().getDisplayMetrics().density), 0, 0);
        EditText editor = new EditText(this);
        editor.setContentDescription("fixture-editor");
        editor.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_MULTI_LINE);
        root.addView(editor);
        TextView sent = new TextView(this);
        sent.setContentDescription("fixture-sent");
        root.addView(sent);
        Button send = new Button(this);
        send.setText("Send");
        send.setContentDescription("fixture-send");
        send.setFocusable(false);
        send.setOnClickListener(v -> {
            sent.setText(editor.getText().toString());
            editor.beginBatchEdit();
            BaseInputConnection.removeComposingSpans(editor.getText());
            editor.setText("");
            editor.endBatchEdit();
        });
        root.addView(send);
        Button finish = new Button(this);
        finish.setText("Finish composing");
        finish.setContentDescription("fixture-finish");
        finish.setFocusable(false);
        finish.setOnClickListener(v -> editor.onCreateInputConnection(new EditorInfo()).finishComposingText());
        root.addView(finish);
        setContentView(root);
        editor.requestFocus();
    }
}
