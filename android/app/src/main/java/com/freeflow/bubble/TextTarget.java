package com.freeflow.bubble;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.os.Bundle;
import android.view.accessibility.AccessibilityNodeInfo;

/** Reads and writes the text field you're typing in, through Accessibility. */
public final class TextTarget {
    public final AccessibilityNodeInfo node;

    public TextTarget(AccessibilityNodeInfo node) { this.node = node; }

    public boolean alive() {
        try { return node.refresh() && node.isEditable(); } catch (Exception e) { return false; }
    }

    /** Current text, without the grey hint/placeholder some apps report as text. */
    public String text() {
        node.refresh();
        if (node.isShowingHintText()) return "";
        CharSequence t = node.getText();
        return t == null ? "" : t.toString();
    }

    public int[] selection(String cur) {
        int s = node.getTextSelectionStart(), e = node.getTextSelectionEnd();
        if (s < 0 || s > cur.length() || e < 0 || e > cur.length()) { s = cur.length(); e = s; }
        if (e < s) { int x = s; s = e; e = x; }
        return new int[]{s, e};
    }

    /** Selected text if there's a selection, otherwise everything. */
    public String selectedOrAll() {
        String cur = text();
        int[] sel = selection(cur);
        return sel[0] != sel[1] ? cur.substring(sel[0], sel[1]) : cur;
    }

    /** Inserts at the cursor (replacing any selection), with a space before when needed. */
    public boolean insert(Context ctx, String ins) {
        String cur = text();
        int[] sel = selection(cur);
        if (sel[0] > 0 && !Character.isWhitespace(cur.charAt(sel[0] - 1)) && !ins.isEmpty()
                && !Character.isWhitespace(ins.charAt(0)) && ".,!?;:)".indexOf(ins.charAt(0)) < 0) ins = " " + ins;
        String next = cur.substring(0, sel[0]) + ins + cur.substring(sel[1]);
        if (setText(next, sel[0] + ins.length())) return true;
        return paste(ctx, ins);
    }

    /** Replaces the selection (or all text) with {@code with}. */
    public boolean replaceSelectedOrAll(Context ctx, String with) {
        String cur = text();
        int[] sel = selection(cur);
        String next;
        int cursor;
        if (sel[0] != sel[1]) { next = cur.substring(0, sel[0]) + with + cur.substring(sel[1]); cursor = sel[0] + with.length(); }
        else { next = with; cursor = with.length(); }
        return setText(next, cursor);
    }

    public boolean setText(String all, int cursor) {
        Bundle a = new Bundle();
        a.putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, all);
        boolean ok = node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, a);
        if (ok) {
            Bundle b = new Bundle();
            int c = Math.max(0, Math.min(cursor, all.length()));
            b.putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_START_INT, c);
            b.putInt(AccessibilityNodeInfo.ACTION_ARGUMENT_SELECTION_END_INT, c);
            node.performAction(AccessibilityNodeInfo.ACTION_SET_SELECTION, b);
        }
        return ok;
    }

    /** Fallback for fields that refuse SET_TEXT (some web views): put it on the clipboard and paste. */
    private boolean paste(Context ctx, String ins) {
        ClipboardManager cm = (ClipboardManager) ctx.getSystemService(Context.CLIPBOARD_SERVICE);
        cm.setPrimaryClip(ClipData.newPlainText("FreeFlow", ins));
        return node.performAction(AccessibilityNodeInfo.ACTION_PASTE);
    }
}
