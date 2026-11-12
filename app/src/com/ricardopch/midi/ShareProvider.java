package com.ricardopch.midi;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;

/** Sirve los MP3 y MIDI generados (carpeta cache/out) para compartirlos con otras apps. */
public class ShareProvider extends ContentProvider {

    static Uri uriFor(Context c, File f) {
        return Uri.parse("content://" + c.getPackageName() + ".share/" + Uri.encode(f.getName()));
    }

    static File outDir(Context c) {
        File d = new File(c.getCacheDir(), "out");
        if (!d.exists()) d.mkdirs();
        return d;
    }

    @Override
    public boolean onCreate() { return true; }

    private File fileFor(Uri uri) throws FileNotFoundException {
        String name = uri.getLastPathSegment();
        if (name == null || name.contains("/") || name.contains("..")) throw new FileNotFoundException();
        Context c = getContext();
        if (c == null) throw new FileNotFoundException();
        File f = new File(outDir(c), name);
        if (!f.isFile()) throw new FileNotFoundException(name);
        return f;
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        return ParcelFileDescriptor.open(fileFor(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        File f;
        try {
            f = fileFor(uri);
        } catch (FileNotFoundException e) {
            return null;
        }
        if (projection == null) projection = new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE};
        String[] cols = new String[projection.length];
        Object[] vals = new Object[projection.length];
        int n = 0;
        for (String col : projection) {
            if (OpenableColumns.DISPLAY_NAME.equals(col)) {
                cols[n] = col;
                vals[n++] = f.getName();
            } else if (OpenableColumns.SIZE.equals(col)) {
                cols[n] = col;
                vals[n++] = f.length();
            }
        }
        String[] c2 = new String[n];
        Object[] v2 = new Object[n];
        System.arraycopy(cols, 0, c2, 0, n);
        System.arraycopy(vals, 0, v2, 0, n);
        MatrixCursor mc = new MatrixCursor(c2, 1);
        mc.addRow(v2);
        return mc;
    }

    @Override
    public String getType(Uri uri) {
        String name = uri.getLastPathSegment();
        return name != null && name.toLowerCase(java.util.Locale.US).endsWith(".mid") ? "audio/midi" : "audio/mpeg";
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) { return null; }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) { return 0; }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) { return 0; }
}
