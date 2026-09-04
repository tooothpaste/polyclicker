// ===========================================================================
//  MacroEditor - view, edit, and build macro takes
// ---------------------------------------------------------------------------
//  A raw take is hundreds of pointer samples a second; nobody edits those one
//  by one. TakeBuffer groups the event stream into GESTURES - a move run, a
//  click (down..up), a drag, a key press, a scroll burst - and the editor
//  shows one row per gesture with its start time, the pause before it, and
//  its length. A row owns a SET of events, not necessarily a contiguous run:
//  a key press is always one row even when other input lands between its
//  down and its up. The rules for deleting, retiming and moving rows sit on
//  the methods that implement them.
//
//  Steps can also be inserted - clicks, key presses, scrolls, moves - so a
//  macro can be built here from nothing, not just recorded and trimmed.
//
//  The file is only touched by Save; everything else happens on an in-memory
//  copy with undo. The written format is the same one Recorder produces, so
//  edited takes load in every version back to 1.0.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Polyclicker
{
    // One event of the take being edited, in file units (ms from take start).
    // Engine's Ev carries QPC ticks; converting back and forth per edit would
    // just add rounding, so the editor stays in the file's own unit.
    struct REv
    {
        public double Ms;
        public byte Type;               // 0 move, 1 down, 2 up, 3 keydown, 4 keyup, 5 wheel
        public int A, B;
    }

    enum RKind { Move, Click, Drag, Key, Scroll, Stray }

    // A gesture: one editable row over a set of event indices (ascending;
    // contiguous for everything except key presses, which may span other
    // gestures - see the header).
    sealed class TakeRow
    {
        public List<int> Idx;
        public double StartMs, EndMs;
        public RKind Kind;
        public string Desc;
        public int DownAt = -1;         // press event, for "set click spot"
        public int X = int.MinValue, Y; // where a click/drag begins on screen
        public int EX = int.MinValue, EY;   // where a move/drag ends

        public int From { get { return Idx[0]; } }
    }

    // The take being edited: events, gesture rows, and every mutation the
    // editor offers - all UI-free, so the regression harness can drive it.
    sealed class TakeBuffer
    {
        List<REv> evs = new List<REv>();
        public readonly List<string> Header = new List<string>();  // # lines, verbatim
        public readonly List<TakeRow> Rows = new List<TakeRow>();
        readonly List<List<REv>> undoStack = new List<List<REv>>();

        public int EventCount { get { return evs.Count; } }
        public int UndoDepth { get { return undoStack.Count; } }
        public double TotalMs { get { return evs.Count > 0 ? evs[evs.Count - 1].Ms : 0; } }

        public REv EventAt(int i) { return evs[i]; }

        static bool ParseEv(string line, out REv e)
        {
            e = new REv();
            string[] f = line.Split(' ');
            if (f.Length < 4) return false;
            if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e.Ms))
                return false;
            byte t; int a, b;
            if (!byte.TryParse(f[1], out t) || !int.TryParse(f[2], out a)
                || !int.TryParse(f[3], out b)) return false;
            e.Type = t; e.A = a; e.B = b;
            return true;
        }

        public void Load(string path)
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#') { Header.Add(line); continue; }
                REv e;
                if (ParseEv(line, out e)) evs.Add(e);
            }
            StableSort();
            BuildRows();
        }

        public void WriteTo(string file, int fromEvent)
        {
            var sb = new StringBuilder();
            if (Header.Count == 0) sb.AppendLine("# Polyclicker macro v1");
            foreach (string h in Header) sb.AppendLine(h);
            AppendEvents(sb, fromEvent);
            AppConfig.WriteAtomic(file, sb.ToString(), Encoding.UTF8);
        }

        void AppendEvents(StringBuilder sb, int fromEvent)
        {
            double baseMs = fromEvent > 0 && fromEvent < evs.Count ? evs[fromEvent].Ms : 0;
            for (int i = fromEvent; i < evs.Count; i++)
                sb.AppendLine((evs[i].Ms - baseMs).ToString("F3", CultureInfo.InvariantCulture)
                    + " " + evs[i].Type + " " + evs[i].A + " " + evs[i].B);
        }

        // The events as text - what "has this take changed?" compares, the
        // same way the main window compares its cards against the profile
        // on disk. Undoing back to the saved state reads as clean, and a
        // cell committed with its own value never counts as an edit.
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            AppendEvents(sb, 0);
            return sb.ToString();
        }

        public void StampEdited()
        {
            Header.RemoveAll(delegate(string h) { return h.StartsWith("# edited "); });
            Header.Add("# edited " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        // Edits can land events on the same millisecond (a zero-length click's
        // down and up); List.Sort would be free to put the up first and leave
        // a button logically pressed, so ties keep their existing order.
        void StableSort()
        {
            int n = evs.Count;
            var order = new int[n];
            for (int k = 0; k < n; k++) order[k] = k;
            List<REv> src = evs;
            Array.Sort(order, delegate(int a, int b)
            {
                int c = src[a].Ms.CompareTo(src[b].Ms);
                return c != 0 ? c : a.CompareTo(b);
            });
            var outp = new List<REv>(n);
            for (int k = 0; k < n; k++) outp.Add(src[order[k]]);
            evs = outp;
        }

        // --- grouping -------------------------------------------------------

        static string BtnName(int b)
        {
            switch (b)
            {
                case 1: return "Right";
                case 2: return "Middle";
                case 3: return "X1";
                case 4: return "X2";
                default: return "Left";
            }
        }

        // The matching release for the press at index i, or -1.
        int FindUp(int i, byte upType, int code)
        {
            for (int k = i + 1; k < evs.Count; k++)
                if (evs[k].Type == upType && evs[k].A == code) return k;
            return -1;
        }

        void NewRow(List<int> idx, RKind kind, string desc, int downAt)
        {
            var r = new TakeRow();
            r.Idx = idx;
            r.StartMs = evs[idx[0]].Ms;
            r.EndMs = evs[idx[idx.Count - 1]].Ms;
            r.Kind = kind; r.Desc = desc; r.DownAt = downAt;
            Rows.Add(r);
        }

        static List<int> One(int i) { var l = new List<int>(); l.Add(i); return l; }

        static List<int> Range(int a, int b)
        {
            var l = new List<int>();
            for (int k = a; k <= b; k++) l.Add(k);
            return l;
        }

        // One pass turns the event list into gesture rows. lastX/Y tracks the
        // pointer so button rows can say WHERE they clicked - press events
        // carry no coordinates, the moves before them do. consumed[] lets a
        // key row claim its far-away release without owning the events
        // between; rows come out ordered by start time because each is
        // created at its first event.
        void BuildRows()
        {
            Rows.Clear();
            int n = evs.Count;
            var consumed = new bool[n];
            int lastX = 0, lastY = 0;
            for (int i = 0; i < n; i++)
            {
                if (consumed[i]) continue;
                REv e = evs[i];
                if (e.Type == 0)                            // move run
                {
                    int j = i;
                    while (j + 1 < n && !consumed[j + 1] && evs[j + 1].Type == 0) j++;
                    lastX = evs[j].A; lastY = evs[j].B;
                    NewRow(Range(i, j), RKind.Move, "Move to " + lastX + ", " + lastY, -1);
                    Rows[Rows.Count - 1].EX = lastX;
                    Rows[Rows.Count - 1].EY = lastY;
                    i = j;
                    continue;
                }
                if (e.Type == 1)                            // click or drag
                {
                    int up = FindUp(i, 2, e.A);
                    if (up < 0)
                    {
                        NewRow(One(i), RKind.Stray,
                            BtnName(e.A) + " button down (never released)", i);
                        continue;
                    }
                    var idx = One(i);
                    int px = lastX, py = lastY, ex = px, ey = py;
                    bool far = false;
                    for (int k = i + 1; k < up; k++)        // the path is the drag's own
                    {
                        if (evs[k].Type != 0 || consumed[k]) continue;
                        idx.Add(k);
                        ex = evs[k].A; ey = evs[k].B;
                        if (Math.Abs(ex - px) > 5 || Math.Abs(ey - py) > 5) far = true;
                    }
                    idx.Add(up);
                    string d = far
                        ? BtnName(e.A) + " drag " + px + ", " + py + " → " + ex + ", " + ey
                        : BtnName(e.A) + " click at " + px + ", " + py;
                    NewRow(idx, far ? RKind.Drag : RKind.Click, d, i);
                    Rows[Rows.Count - 1].X = px;
                    Rows[Rows.Count - 1].Y = py;
                    Rows[Rows.Count - 1].EX = ex;
                    Rows[Rows.Count - 1].EY = ey;
                    foreach (int k in idx) consumed[k] = true;
                    lastX = ex; lastY = ey;
                    continue;
                }
                if (e.Type == 2)                            // release with no press
                {
                    NewRow(One(i), RKind.Stray, BtnName(e.A) + " button up (no press)", -1);
                    continue;
                }
                if (e.Type == 3)                            // key press, always one row
                {
                    string name = HotkeyParser.VkToName((ushort)e.A);
                    int up = FindUp(i, 4, e.A);
                    var idx = One(i);
                    int reps = 1, limit = up < 0 ? n : up;
                    for (int k = i + 1; k < limit; k++)     // auto-repeat downs
                        if (!consumed[k] && evs[k].Type == 3 && evs[k].A == e.A)
                        { idx.Add(k); reps++; }
                    if (up >= 0)
                    {
                        idx.Add(up);
                        NewRow(idx, RKind.Key, "Key " + name + (reps > 1 ? " (held)" : ""), -1);
                    }
                    else
                        NewRow(idx, RKind.Stray, "Key " + name + " down (never released)", -1);
                    foreach (int k in idx) consumed[k] = true;
                    continue;
                }
                if (e.Type == 4)                            // release with no press
                {
                    NewRow(One(i), RKind.Stray,
                        "Key " + HotkeyParser.VkToName((ushort)e.A) + " up (no press)", -1);
                    continue;
                }
                if (e.Type == 5)                            // scroll burst
                {
                    int j = i, sum = e.A;
                    while (j + 1 < n && evs[j + 1].Type == 5
                           && Math.Sign(evs[j + 1].A) == Math.Sign(e.A)
                           && evs[j + 1].Ms - evs[j].Ms < 400)
                    {
                        j++;
                        sum += evs[j].A;
                    }
                    int notches = Math.Max(1, Math.Abs(sum) / 120);
                    NewRow(Range(i, j), RKind.Scroll,
                        "Scroll " + (e.A > 0 ? "up" : "down") + " ×" + notches, -1);
                    i = j;
                    continue;
                }
                NewRow(One(i), RKind.Stray, "Event type " + e.Type, -1);    // future-proof
            }

            // A key's release can land after later rows begin - a modifier
            // held across clicks, typing rollover. The list orders rows by
            // when they START, which would read as "released, then..." - say
            // what actually happens instead.
            for (int r = 0; r < Rows.Count; r++)
            {
                if (Rows[r].Kind != RKind.Key) continue;
                int spanned = 0;
                for (int q = r + 1; q < Rows.Count
                     && Rows[q].StartMs < Rows[r].EndMs - 0.0005; q++)
                    spanned++;
                if (spanned == 0) continue;
                string d = Rows[r].Desc;
                if (d.EndsWith(" (held)")) d = d.Substring(0, d.Length - 7);
                Rows[r].Desc = d + "  (held across the next "
                    + (spanned == 1 ? "step" : spanned + " steps") + ")";
            }
        }

        public double GapOf(int rowIdx)
        {
            TakeRow r = Rows[rowIdx];
            return r.StartMs - (rowIdx > 0 ? Rows[rowIdx - 1].EndMs : 0);
        }

        // The row that starts at (or first after) the given time.
        public int FindRowAt(double ms)
        {
            for (int i = 0; i < Rows.Count; i++)
                if (Rows[i].StartMs >= ms - 0.01) return i;
            return Rows.Count - 1;
        }

        // --- mutations --------------------------------------------------------

        void PushUndo()
        {
            undoStack.Add(new List<REv>(evs));
            if (undoStack.Count > 100) undoStack.RemoveAt(0);
        }

        public bool Undo()
        {
            if (undoStack.Count == 0) return false;
            evs = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            BuildRows();
            return true;
        }

        // Every press whose release would survive the deletion (or the other
        // way round) pulls its partner in - including a held key's auto-repeat
        // downs, which all belong to the one release. Grows until stable.
        void ClosePairs(HashSet<int> del)
        {
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (int idx in new List<int>(del))
                {
                    REv e = evs[idx];
                    if (e.Type == 1 || e.Type == 3)
                    {
                        for (int k = idx + 1; k < evs.Count; k++)
                        {
                            if (evs[k].A != e.A) continue;
                            if (evs[k].Type == e.Type)      // an auto-repeat down
                            {
                                if (del.Add(k)) grew = true;
                                continue;
                            }
                            if (evs[k].Type == e.Type + 1)  // its release
                            {
                                if (del.Add(k)) grew = true;
                                break;
                            }
                        }
                    }
                    else if (e.Type == 2 || e.Type == 4)
                    {
                        for (int k = idx - 1; k >= 0; k--)
                        {
                            if (evs[k].A != e.A) continue;
                            if (evs[k].Type == e.Type) break;       // an earlier release: done
                            if (evs[k].Type == e.Type - 1)          // a down this released
                            {
                                if (del.Add(k)) grew = true;
                                // keep walking - auto-repeats stack downs
                            }
                        }
                    }
                }
            }
        }

        public void DeleteRows(ICollection<int> rowIndices)
        {
            if (rowIndices.Count == 0) return;
            PushUndo();

            var del = new HashSet<int>();
            var spans = new List<double[]>();
            foreach (int ri in rowIndices)
            {
                foreach (int k in Rows[ri].Idx) del.Add(k);
                spans.Add(new double[] { Rows[ri].StartMs, Rows[ri].EndMs });
            }
            ClosePairs(del);

            // A span only closes when nothing survives inside it - deleting a
            // Shift held across three clicks removes the shift, not the time
            // the clicks still need. Pair strays pulled in above are instants
            // and close no time either way.
            // Survivors in time order (evs is sorted), so each span is one
            // binary search rather than a pass over the take
            var alive = new List<double>(evs.Count);
            for (int k = 0; k < evs.Count; k++) if (!del.Contains(k)) alive.Add(evs[k].Ms);
            var closable = new List<double[]>();
            foreach (double[] sp in spans)
            {
                int i = alive.BinarySearch(sp[0] + 0.0005);
                if (i < 0) i = ~i;
                bool occupied = i < alive.Count && alive[i] < sp[1] - 0.0005;
                if (!occupied) closable.Add(sp);
            }
            closable.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            // Nested or overlapping spans (a key held across a click, both
            // going) close their shared time once, not twice
            var merged = new List<double[]>();
            foreach (double[] sp in closable)
            {
                double[] last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && sp[0] <= last[1] + 0.0005) last[1] = Math.Max(last[1], sp[1]);
                else merged.Add(new double[] { sp[0], sp[1] });
            }
            closable = merged;

            var kept = new List<REv>(evs.Count - del.Count);
            for (int k = 0; k < evs.Count; k++)
            {
                if (del.Contains(k)) continue;
                REv e = evs[k];
                double shift = 0;
                foreach (double[] sp in closable)
                {
                    if (sp[1] <= e.Ms) shift += sp[1] - sp[0];
                    else break;
                }
                e.Ms -= shift;
                kept.Add(e);
            }
            evs = kept;
            BuildRows();
        }

        // Change the pause before a row; the row and everything after shift.
        public void SetGap(int rowIdx, double gapMs)
        {
            if (gapMs < 0) gapMs = 0;
            double delta = gapMs - GapOf(rowIdx);
            if (Math.Abs(delta) < 0.5) return;
            PushUndo();
            for (int k = Rows[rowIdx].From; k < evs.Count; k++)
            {
                REv e = evs[k];
                e.Ms += delta;
                evs[k] = e;
            }
            BuildRows();
        }

        // Stretch or shrink a gesture: its own events scale across the new
        // length, and everything at or past its old end slides by the change.
        public void SetLength(int rowIdx, double newDur)
        {
            TakeRow r = Rows[rowIdx];
            if (r.Idx.Count < 2 || newDur < 0) return;
            double start = r.StartMs, oldDur = r.EndMs - r.StartMs;
            if (Math.Abs(newDur - oldDur) < 0.5) return;
            PushUndo();
            var inRow = new HashSet<int>(r.Idx);
            for (int k = 0; k < r.Idx.Count; k++)
            {
                int ei = r.Idx[k];
                REv e = evs[ei];
                e.Ms = oldDur > 0
                    ? start + (e.Ms - start) * (newDur / oldDur)
                    : start + newDur * k / (r.Idx.Count - 1);
                evs[ei] = e;
            }
            double delta = newDur - oldDur, oldEnd = start + oldDur;
            for (int k = 0; k < evs.Count; k++)
            {
                if (inRow.Contains(k) || evs[k].Ms < oldEnd - 0.0005) continue;
                REv e = evs[k];
                e.Ms += delta;
                evs[k] = e;
            }
            StableSort();
            BuildRows();
        }

        // Repoint a click: a move event goes in right before the press, which
        // is where playback reads a click's coordinates from anyway.
        public void SetClickSpot(int rowIdx, int x, int y)
        {
            TakeRow r = Rows[rowIdx];
            if (r.DownAt < 0 || (r.X == x && r.Y == y)) return;
            PushUndo();
            var mv = new REv();
            mv.Ms = evs[r.DownAt].Ms;
            mv.Type = 0; mv.A = x; mv.B = y;
            evs.Insert(r.DownAt, mv);
            BuildRows();
        }

        // Blend an offset across a row's pointer samples: ds at the first,
        // de at the last, linear between - so a pure translation (ds == de)
        // slides the whole path and a changed endpoint bends it, anchored
        // where it starts.
        void MorphMoves(TakeRow r, int dsx, int dsy, int dex, int dey)
        {
            var moves = new List<int>();
            foreach (int k in r.Idx) if (evs[k].Type == 0) moves.Add(k);
            int n = moves.Count;
            for (int i = 0; i < n; i++)
            {
                double t = (i + 1) / (double)n;
                REv e = evs[moves[i]];
                e.A += (int)Math.Round(dsx * (1 - t) + dex * t);
                e.B += (int)Math.Round(dsy * (1 - t) + dey * t);
                evs[moves[i]] = e;
            }
        }

        // Repoint whatever coordinates a row has. A click takes a new spot;
        // a move run bends to land on a new endpoint; a drag takes a new
        // start and end - its path translates with the start and bends to
        // the end.
        public void SetCoords(int rowIdx, int sx, int sy, int ex, int ey)
        {
            TakeRow r = Rows[rowIdx];
            if (r.Kind == RKind.Click) { SetClickSpot(rowIdx, sx, sy); return; }
            if (r.Kind == RKind.Move)
            {
                if (ex == r.EX && ey == r.EY) return;
                PushUndo();
                MorphMoves(r, 0, 0, ex - r.EX, ey - r.EY);
                BuildRows();
                return;
            }
            if (r.Kind != RKind.Drag || r.DownAt < 0) return;
            if (sx == r.X && sy == r.Y && ex == r.EX && ey == r.EY) return;
            PushUndo();
            MorphMoves(r, sx - r.X, sy - r.Y, ex - r.EX, ey - r.EY);
            var mv = new REv();
            mv.Ms = evs[r.DownAt].Ms;
            mv.Type = 0; mv.A = sx; mv.B = sy;
            evs.Insert(r.DownAt, mv);
            BuildRows();
        }

        // Insert a step (its events on their own zero-based clock) after the
        // given row, gapMs later; -1 or an out-of-range row appends at the
        // end. Everything past the anchor slides down to make room. Returns
        // the time the step landed at, for reselecting its row.
        public double InsertStep(int afterRow, REv[] step, double gapMs)
        {
            double stepLen = 0;
            foreach (REv s in step) stepLen = Math.Max(stepLen, s.Ms);
            PushUndo();
            double at;
            if (evs.Count == 0)
                at = 0;
            else if (afterRow < 0)                  // before everything
            {
                at = 0;
                for (int k = 0; k < evs.Count; k++)
                {
                    REv e = evs[k];
                    e.Ms += stepLen + gapMs;
                    evs[k] = e;
                }
            }
            else
            {
                double anchor = afterRow < Rows.Count ? Rows[afterRow].EndMs : TotalMs;
                at = anchor + gapMs;
                for (int k = 0; k < evs.Count; k++)
                {
                    if (evs[k].Ms <= anchor + 0.0005) continue;
                    REv e = evs[k];
                    e.Ms += gapMs + stepLen;
                    evs[k] = e;
                }
            }
            foreach (REv s in step)
            {
                REv e = s;
                e.Ms = at + s.Ms;
                evs.Add(e);
            }
            StableSort();
            BuildRows();
            return at;
        }

        // The selected rows' events on their own zero-based clock - the shape
        // InsertStep takes back, so extract+insert is duplicate, and extract+
        // text is the clipboard.
        public REv[] Extract(List<int> rowIndices)
        {
            var idx = new List<int>();
            foreach (int ri in rowIndices) idx.AddRange(Rows[ri].Idx);
            idx.Sort();
            var outp = new REv[idx.Count];
            double base0 = idx.Count > 0 ? evs[idx[0]].Ms : 0;
            for (int i = 0; i < idx.Count; i++)
            {
                outp[i] = evs[idx[i]];
                outp[i].Ms -= base0;
            }
            return outp;
        }

        // The clipboard format is the file format: steps copied here paste
        // into any open editor, and a take pasted into a text editor reads
        // as macro lines.
        public string ExtractText(List<int> rowIndices)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Polyclicker steps");
            foreach (REv e in Extract(rowIndices))
                sb.AppendLine(e.Ms.ToString("F3", CultureInfo.InvariantCulture)
                    + " " + e.Type + " " + e.A + " " + e.B);
            return sb.ToString();
        }

        public static REv[] ParseSteps(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var list = new List<REv>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                REv e;
                if (ParseEv(line, out e)) list.Add(e);
            }
            if (list.Count == 0) return null;
            double m = double.MaxValue;
            foreach (REv e in list) m = Math.Min(m, e.Ms);
            for (int i = 0; i < list.Count; i++)
            {
                REv e = list[i];
                e.Ms -= m;
                list[i] = e;
            }
            return list.ToArray();
        }

        // Move a solid block of rows one row up or down by swapping it with
        // its neighbor: the block takes the neighbor's start slot, the
        // neighbor slides past it, and the pause between them survives the
        // trade. Row boundaries can share a millisecond (a move run flows
        // straight into its click), so membership is by EVENT, never by time
        // window. One undo entry. Returns the block's landing time, or -1
        // when the move can't happen (gappy selection, already at an end).
        public double MoveRows(List<int> selIn, bool down)
        {
            if (selIn.Count == 0) return -1;
            var sel = new List<int>(selIn);
            sel.Sort();
            int a = sel[0], b = sel[sel.Count - 1];
            if (b - a + 1 != sel.Count) return -1;
            if (!down && a == 0) return -1;
            if (down && b == Rows.Count - 1) return -1;

            var xIdx = new HashSet<int>();
            double x0 = Rows[a].StartMs, x1 = Rows[a].EndMs;
            foreach (int ri in sel)
            {
                foreach (int k in Rows[ri].Idx) xIdx.Add(k);
                x1 = Math.Max(x1, Rows[ri].EndMs);
            }
            int other = down ? b + 1 : a - 1;
            var pIdx = new HashSet<int>(Rows[other].Idx);
            double p0 = Rows[other].StartMs, p1 = Rows[other].EndMs;
            // Rows that overlap in time (a key held across its neighbour)
            // have no order to swap; the arithmetic below would go negative
            if (down ? x1 > p0 + 0.0005 : p1 > x0 + 0.0005) return -1;

            double shiftX, shiftP;
            if (down)
            {
                double gap = p0 - x1;               // pause between block and neighbor
                shiftP = x0 - p0;                   // neighbor takes the block's slot
                shiftX = (p1 - p0) + gap;           // block lands past it, pause kept
            }
            else
            {
                double gap = x0 - p1;
                shiftX = p0 - x0;
                shiftP = (x1 - x0) + gap;
            }

            PushUndo();
            for (int k = 0; k < evs.Count; k++)
            {
                double d = xIdx.Contains(k) ? shiftX : pIdx.Contains(k) ? shiftP : 0;
                if (d == 0) continue;
                REv e = evs[k];
                e.Ms += d;
                evs[k] = e;
            }
            StableSort();
            BuildRows();
            return x0 + shiftX;
        }
    }


    // --- the editor window ---------------------------------------------------
    //  Modeless and resizable: it floats beside the main window as a
    //  workspace rather than locking it, so cards stay usable while a take
    //  is being shaped. The whole thing drives from the keyboard too - see
    //  the key map on the list's tooltip.
    sealed class MacroEditorDialog : AppDialog
    {
        // Theme.S on this window's own monitor
        int S(int v) { return (int)Math.Round(Theme.S(v) * LocalScale); }

        string path;
        readonly Action<string, string> onRenamed;      // old file name, new file name
        readonly TakeBuffer take = new TakeBuffer();
        // What the file holds, as the buffer renders it - dirtiness is a
        // comparison against this, never a flag that has to be kept honest
        string savedPrint = "";
        bool Dirty { get { return take.Fingerprint() != savedPrint; } }

        readonly ListBox list = new ListBox();
        readonly Field nameBox = new Field();
        // One shared in-place editor, parented to the list and moved over
        // whichever cell is being edited. A plain Field, not a NumberBox:
        // the position cell holds "x, y" and needs its comma.
        readonly Field cellEdit = new Field();
        enum Cell { Wait, Len, Spot }
        int cellRow = -1;
        Cell cellKind;
        bool committingName;
        // True while THIS code is changing the selection or rebuilding the
        // items. ListBox raises SelectedIndexChanged from inside ClearSelected
        // and Items.Clear while its own selection state is mid-mutation;
        // reading SelectedIndices there can throw from deep in the control.
        bool adjusting;
        Label info;
        ChipButton addBtn, delBtn, playBtn, upBtn, downBtn;
        Font boldFont;
        readonly ToolTip tip = new ToolTip();

        // Column x positions, shared by the header labels and the row painter
        const int ColTime = 10, ColGap = 78, ColDesc = 168, ColDurW = 84;

        // Each editor gets its own engine slot id, far above anything a card
        // can own, so two open editors' previews cannot cross wires
        static int nextPreviewId = 1000000;
        readonly int previewId = nextPreviewId++;
        bool previewing;
        Point homeLoc;
        Action<int> stoppedHook;

        public string TakePath { get { return path; } }

        static readonly string[] StepNames = new string[]
        {
            "Left click", "Double click", "Right click", "Middle click",
            "Key press…", "Scroll up", "Scroll down", "Move to…"
        };

        public MacroEditorDialog(string filePath, string shownName,
                                 Action<string, string> renamed)
            : base(shownName + " - Edit take")
        {
            path = filePath;
            onRenamed = renamed;
            take.Load(path);
            savedPrint = take.Fingerprint();

            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            const int Mx = 12;
            const int W = 860;
            int y = Mx;

            // The take's name, editable in place like a card's - committing
            // it renames the file
            nameBox.SetBounds(Mx, y, 240, 25);
            nameBox.Text = Path.GetFileNameWithoutExtension(path);
            Controls.Add(nameBox);
            tip.SetToolTip(nameBox, "The macro's name - edit it to rename the file"
                + " (Enter commits, Esc reverts)");
            nameBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitName();
                    list.Focus();
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            nameBox.LostFocus += delegate { CommitName(); };

            info = Plain("", Mx + 252, y + 4, W - 252);
            info.ForeColor = Color.Gray;
            y += 32;

            // Column captions sit on the same grid the rows use
            Plain("start", Mx + ColTime, y, 0).ForeColor = Color.Gray;
            Plain("wait", Mx + ColGap, y, 0).ForeColor = Color.Gray;
            Plain("action", Mx + ColDesc, y, 0).ForeColor = Color.Gray;
            Label durCap = Plain("length", Mx + W - ColDurW, y, ColDurW - 8);
            durCap.ForeColor = Color.Gray;
            durCap.TextAlign = ContentAlignment.TopRight;
            y += 20;

            list.SetBounds(Mx, y, W, 330);
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = S(22);
            list.SelectionMode = SelectionMode.MultiExtended;
            list.IntegralHeight = false;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.DrawItem += DrawRow;
            list.SelectedIndexChanged += delegate { OnSelection(); };
            list.KeyDown += OnListKey;
            // A widened owner-drawn list only repaints the newly exposed
            // strip, smearing the right-aligned column across it - every
            // resize repaints everything (and closes any in-place edit,
            // whose cell just moved)
            list.Resize += delegate { EndCellEdit(false); list.Invalidate(); };
            list.MouseDown += OnListMouse;
            Controls.Add(list);

            cellEdit.Visible = false;
            cellEdit.TextAlign = HorizontalAlignment.Right;
            list.Controls.Add(cellEdit);
            // Excel's grammar: Enter confirms and leaves the cell, Tab
            // confirms and moves on - wait, length, next row's wait - and
            // Shift+Tab walks the same path backwards
            cellEdit.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e)
            {
                if (e.KeyCode == Keys.Tab) e.IsInputKey = true;
            };
            cellEdit.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    EndCellEdit(true);
                    list.Focus();
                    e.Handled = e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Tab)
                {
                    TabCell(e.Shift);
                    e.Handled = e.SuppressKeyPress = true;
                }
            };
            // No refocusing here: this also fires when the user is moving
            // focus somewhere else on purpose, and yanking it back mid-
            // transition is how focus fights start
            cellEdit.LostFocus += delegate { EndCellEdit(true); };
            // The same "show me where" the cards' preview gives: while a
            // position is being edited, a dot marks the spot it names,
            // following every keystroke that still parses
            cellEdit.TextChanged += delegate
            {
                if (cellRow < 0 || cellKind != Cell.Spot) return;
                Point sp, ep;
                int pairs;
                if (ParseSpot(cellEdit.Text, out sp, out ep, out pairs))
                    SpotDot.Pop(sp.X, sp.Y);
            };

            y = list.Bottom + 10;

            // --- the one toolbar row: icon chips, a tooltip each --------------
            int bx = Mx;
            addBtn = IconBtn("plus", "", bx, y, 34, 28); bx += 38;
            addBtn.Click += delegate { OpenAddMenu(); };
            tip.SetToolTip(addBtn, "Add a step after the selection (Ins)");

            delBtn = IconBtn("trash", "", bx, y, 34, 28); bx += 38;
            delBtn.Click += delegate { DeleteSelected(); };
            tip.SetToolTip(delBtn, "Delete the selected steps (Del)");

            ChipButton undoBtn = IconBtn("undo", "", bx, y, 34, 28); bx += 38;
            undoBtn.Click += delegate { Undo(); };
            tip.SetToolTip(undoBtn, "Undo (Ctrl+Z)");

            upBtn = IconBtn("up", "", bx, y, 34, 28); bx += 38;
            upBtn.Click += delegate { MoveSel(false); };
            tip.SetToolTip(upBtn, "Move the selected steps up (Ctrl+Up)");

            downBtn = IconBtn("down", "", bx, y, 34, 28);
            downBtn.Click += delegate { MoveSel(true); };
            tip.SetToolTip(downBtn, "Move the selected steps down (Ctrl+Down)");

            // No close chip: the title bar already has one, and Esc works
            ChipButton copyBtn = IconBtn("copy", "", Mx + W - 110, y, 34, 28);
            copyBtn.Click += delegate { SaveCopy(); };
            tip.SetToolTip(copyBtn, "Save a copy of this take under a new name");

            playBtn = IconBtn("play", "", Mx + W - 72, y, 34, 28);
            playBtn.Click += delegate { Preview(); };
            tip.SetToolTip(playBtn, "Preview once from the selected row (F5)");

            ChipButton save = IconBtn("save", "", Mx + W - 34, y, 34, 28);
            save.Style = delegate { return Theme.GoChip; };
            save.Click += delegate { SaveFile(); };
            tip.SetToolTip(save, "Save this take (Ctrl+S)");
            ClientSize = new Size(W + Mx * 2, y + 28 + Mx);
            ApplyScale();               // now, not at load: modeless, and
            MinimumSize = Size;         // the caller centers it by its size

            // Anchors only AFTER the form has its real size - assigned any
            // earlier they would measure against the default 300x300 client
            // and drag every control out of the window when ClientSize lands.
            // The list soaks up any growth; the toolbar rides the bottom edge
            // and the right-aligned chips ride the right one too.
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                        | AnchorStyles.Left | AnchorStyles.Right;
            durCap.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addBtn.Anchor = delBtn.Anchor = undoBtn.Anchor =
                upBtn.Anchor = downBtn.Anchor =
                AnchorStyles.Bottom | AnchorStyles.Left;
            copyBtn.Anchor = playBtn.Anchor = save.Anchor =
                AnchorStyles.Bottom | AnchorStyles.Right;

            Disposed += delegate
            {
                tip.Dispose();
                if (boldFont != null) boldFont.Dispose();
                if (stoppedHook != null) Engine.Stopped -= stoppedHook;
                if (previewing) Engine.Stop(previewId);
            };
            // No "save before closing?" question - same as switching away
            // from an unsaved profile. Save is explicit (the chip, Ctrl+S);
            // closing discards, and the title bar's asterisk says so first.
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                bool typing = cellEdit.Focused || nameBox.Focused;
                if (e.Control && e.KeyCode == Keys.Z && !typing)
                { Undo(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.S)
                { SaveFile(); e.Handled = e.SuppressKeyPress = true; }
            };

            // The preview's end brings the window back from off-screen
            stoppedHook = delegate(int id)
            {
                if (id != previewId) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        previewing = false;
                        playBtn.Kind = "play";
                        playBtn.Invalidate();
                        Location = homeLoc;
                        Activate();
                    });
                }
                catch { }
            };
            Engine.Stopped += stoppedHook;

            RefreshRows(0);
            ActiveControl = list;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            boldFont = new Font(Font, FontStyle.Bold);
        }

        // The list's row height and the bold row font were sized at open
        protected override void OnScaleChanged()
        {
            list.ItemHeight = S(22);
            if (boldFont != null) boldFont.Dispose();
            boldFont = new Font(Font, FontStyle.Bold);
            MinimumSize = Size;
        }

        // Escape backs out of whatever is innermost: an in-place cell edit,
        // then an uncommitted name, then the window itself
        protected override bool EscapeCloses()
        {
            if (cellEdit.Visible) { EndCellEdit(false); list.Focus(); return false; }
            if (nameBox.Focused)
            {
                nameBox.Text = Path.GetFileNameWithoutExtension(path);
                list.Focus();
                return false;
            }
            return true;
        }

        // Rebuild the list after any change. keepNear keeps the view (and
        // selection) roughly where the user was working.
        void RefreshRows(int keepNear)
        {
            adjusting = true;
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < take.Rows.Count; i++) list.Items.Add("");
            list.EndUpdate();
            adjusting = false;
            if (take.Rows.Count > 0)
            {
                int sel = Math.Max(0, Math.Min(keepNear, take.Rows.Count - 1));
                SelectOnly(sel);
                list.TopIndex = Math.Max(0, sel - 6);
            }
            else OnSelection();
            bool dirty = Dirty;
            info.Text = take.EventCount + " events, " + take.Rows.Count + " steps, "
                      + FmtDur(take.TotalMs) + " long"
                      + (dirty ? "  -  unsaved changes" : "");
            Text = Path.GetFileNameWithoutExtension(path) + (dirty ? " *" : "") + " - Edit take";
        }

        // The one way this code ever changes the selection: events suppressed
        // during the mutation, state recomputed once after it settles
        void SelectOnly(int row)
        {
            adjusting = true;
            try
            {
                list.ClearSelected();
                if (row >= 0 && row < list.Items.Count) list.SetSelected(row, true);
            }
            finally { adjusting = false; }
            OnSelection();
        }

        // --- drawing ----------------------------------------------------------

        static string FmtTime(double ms)
        {
            int t = (int)Math.Round(ms);
            return (t / 60000) + ":" + ((t / 1000) % 60).ToString("00")
                 + "." + (t % 1000).ToString("000");
        }

        static string FmtDur(double ms)
        {
            if (ms < 1) return "";
            if (ms < 1000) return ((int)Math.Round(ms)) + " ms";
            return (ms / 1000.0).ToString("0.0") + " s";
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= take.Rows.Count) return;
            TakeRow r = take.Rows[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;

            Color back = sel
                ? (Theme.Dark ? Color.FromArgb(52, 66, 86) : Color.FromArgb(214, 230, 247))
                : (e.Index % 2 == 1
                    ? (Theme.Dark ? Color.FromArgb(35, 35, 40) : Color.FromArgb(248, 248, 250))
                    : Theme.FieldBack);
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);

            // Moves are the take's connective tissue - dimmed so the actions
            // stand out; anything that could strand a press is flagged amber
            Color descC = Theme.FieldText;
            Font f = Font;
            switch (r.Kind)
            {
                case RKind.Move: descC = Theme.MutedText; break;
                case RKind.Click:
                case RKind.Drag: f = boldFont ?? Font; break;
                case RKind.Stray: descC = Theme.WarnText; break;
            }

            // The cell being edited stays blank under its editor box - text
            // peeking out around the box reads as two values fighting
            bool editing = cellEdit.Visible && e.Index == cellRow;

            int ty = e.Bounds.Y + (e.Bounds.Height - Font.Height) / 2;
            TextRenderer.DrawText(e.Graphics, FmtTime(r.StartMs), Font,
                new Point(e.Bounds.X + S(ColTime), ty), Theme.MutedText);
            double gap = take.GapOf(e.Index);
            if (gap >= 1 && !(editing && cellKind == Cell.Wait))
                TextRenderer.DrawText(e.Graphics, "+" + FmtDur(gap), Font,
                    new Point(e.Bounds.X + S(ColGap), ty), Theme.MutedText);
            if (!(editing && cellKind == Cell.Spot))
                TextRenderer.DrawText(e.Graphics, r.Desc, f,
                    new Point(e.Bounds.X + S(ColDesc), ty), descC);
            string dur = FmtDur(r.EndMs - r.StartMs);
            if (dur.Length > 0 && !(editing && cellKind == Cell.Len))
                TextRenderer.DrawText(e.Graphics, dur, Font,
                    new Rectangle(e.Bounds.Right - S(ColDurW), ty, S(ColDurW - 8), Font.Height),
                    Theme.MutedText, TextFormatFlags.Right);
        }

        // --- keyboard and mouse -----------------------------------------------

        // The standard grammar: Ctrl+C/X/V/D for the clipboard, Alt+arrows to
        // move steps, Ins/Del to add and remove, Enter/F2 to edit, F5 to run
        void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.C: CopySel(false); break;
                    case Keys.X: CopySel(true); break;
                    case Keys.V: PasteSteps(); break;
                    case Keys.D: DuplicateSel(); break;
                    case Keys.Up: MoveSel(false); break;
                    case Keys.Down: MoveSel(true); break;
                    default: return;
                }
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                MoveSel(e.KeyCode == Keys.Down);
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            switch (e.KeyCode)
            {
                case Keys.Delete: DeleteSelected(); break;
                case Keys.Enter:
                case Keys.F2: BeginCellEdit(list.SelectedIndex, Cell.Wait); break;
                case Keys.Insert: OpenAddMenu(); break;
                case Keys.F5: Preview(); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }

        List<int> SelRows()
        {
            var l = new List<int>();
            foreach (int ri in list.SelectedIndices) l.Add(ri);
            l.Sort();
            return l;
        }

        // Whoever last touched the clipboard may still hold it open - a
        // clipboard manager, the input synthesizer in a test - and the
        // WinForms calls throw instead of waiting, so wait briefly ourselves
        static bool ClipSet(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); return true; }
                catch { System.Threading.Thread.Sleep(30); }
            }
            return false;
        }

        static string ClipGet()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { System.Threading.Thread.Sleep(30); }
            }
            return null;
        }

        void CopySel(bool cut)
        {
            var sel = SelRows();
            if (previewing || sel.Count == 0) return;
            if (!ClipSet(take.ExtractText(sel)))
            { CursorToast.Pop("The clipboard is busy"); return; }
            // No "copied" toast: copy is silent everywhere else, and popping
            // a window from a key handler can grab activation out from under
            // the editor, leaving the next keystroke going nowhere
            if (cut)
            {
                take.DeleteRows(sel);
                Touched(sel[0]);
            }
        }

        void PasteSteps()
        {
            if (previewing) return;
            REv[] step = TakeBuffer.ParseSteps(ClipGet());
            if (step == null) { CursorToast.Pop("Nothing pasteable on the clipboard"); return; }
            var sel = SelRows();
            Insert(sel.Count > 0 ? sel[sel.Count - 1] : take.Rows.Count - 1, step);
        }

        void DuplicateSel()
        {
            var sel = SelRows();
            if (previewing || sel.Count == 0) return;
            Insert(sel[sel.Count - 1], take.Extract(sel));
        }

        void MoveSel(bool down)
        {
            var sel = SelRows();
            if (previewing || sel.Count == 0) return;
            double at = take.MoveRows(sel, down);
            if (at < 0) return;
            int start = take.FindRowAt(at);
            RefreshRows(start);
            // The whole block stays selected so another nudge keeps moving it
            adjusting = true;
            for (int i = 1; i < sel.Count && start + i < list.Items.Count; i++)
                list.SetSelected(start + i, true);
            adjusting = false;
            OnSelection();
            // A button click parked the focus on the button; the list is
            // where the keyboard should keep working
            if (!list.Focused) list.Focus();
        }

        void OnListMouse(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (Control.ModifierKeys != Keys.None) return;      // range-selecting
            int row = list.IndexFromPoint(e.Location);
            if (row < 0 || row >= take.Rows.Count) return;
            Rectangle ir = list.GetItemRectangle(row);
            if (e.Clicks != 1) return;
            if (e.X >= ir.X + S(ColGap - 2) && e.X < ir.X + S(ColDesc - 12))
                BeginCellEdit(row, Cell.Wait);
            else if (e.X >= ir.Right - S(ColDurW))
                BeginCellEdit(row, Cell.Len);
            // The action text of a coordinate row is its position cell -
            // one click to edit, like the others (rows without a coordinate
            // just select)
            else if (e.X >= ir.X + S(ColDesc) && e.X < ir.Right - S(ColDurW))
                BeginCellEdit(row, Cell.Spot);
        }

        // --- in-place wait / length editing -----------------------------------

        // The cells a row offers, in Tab order: every row has a wait, most
        // have a length, and anything with a coordinate - a click, a move
        // run, a drag - has an editable position too
        bool HasCell(int row, Cell kind)
        {
            TakeRow r = take.Rows[row];
            switch (kind)
            {
                case Cell.Len: return r.Idx.Count >= 2;
                case Cell.Spot: return r.Kind == RKind.Move
                    || r.Kind == RKind.Click || r.Kind == RKind.Drag;
                default: return true;
            }
        }

        // "x, y" or "x, y → x, y" (the arrow "->" works too). One pair on a
        // drag means "slide the whole thing" - the end keeps its distance.
        static bool ParseSpot(string text, out Point s, out Point e, out int pairs)
        {
            s = e = Point.Empty;
            pairs = 0;
            string[] halves = (text ?? "").Replace("->", "→").Split('→');
            if (halves.Length < 1 || halves.Length > 2) return false;
            var pts = new Point[2];
            for (int i = 0; i < halves.Length; i++)
            {
                string[] p = halves[i].Split(new[] { ',', ';', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                int x, y;
                if (p.Length != 2 || !int.TryParse(p[0], out x)
                    || !int.TryParse(p[1], out y)) return false;
                pts[i] = new Point(x, y);
            }
            s = pts[0];
            e = halves.Length == 2 ? pts[1] : pts[0];
            pairs = halves.Length;
            return true;
        }

        void BeginCellEdit(int row, Cell kind)
        {
            EndCellEdit(true);
            if (previewing || row < 0 || row >= take.Rows.Count) return;
            if (!HasCell(row, kind)) return;
            TakeRow r = take.Rows[row];

            SelectOnly(row);
            int vis = Math.Max(1, list.ClientSize.Height / list.ItemHeight);
            if (row < list.TopIndex) list.TopIndex = row;
            else if (row >= list.TopIndex + vis) list.TopIndex = row - vis + 1;

            Rectangle ir = list.GetItemRectangle(row);
            cellRow = row;                  // before Text: its change handler
            cellKind = kind;                // previews spot edits live
            switch (kind)
            {
                case Cell.Wait:
                    cellEdit.Bounds = new Rectangle(ir.X + S(ColGap - 2), ir.Y,
                        S(ColDesc - ColGap - 14), ir.Height);
                    cellEdit.Text = ((int)Math.Round(Math.Max(0, take.GapOf(row)))).ToString();
                    break;
                case Cell.Len:
                    cellEdit.Bounds = new Rectangle(ir.Right - S(ColDurW), ir.Y,
                        S(ColDurW - 6), ir.Height);
                    cellEdit.Text = ((int)Math.Round(r.EndMs - r.StartMs)).ToString();
                    break;
                case Cell.Spot:
                    bool two = r.Kind == RKind.Drag;
                    cellEdit.Bounds = new Rectangle(ir.X + S(ColDesc), ir.Y,
                        S(two ? 200 : 120), ir.Height);
                    cellEdit.Text = r.Kind == RKind.Move
                        ? r.EX + ", " + r.EY
                        : two ? r.X + ", " + r.Y + " → " + r.EX + ", " + r.EY
                              : r.X + ", " + r.Y;
                    break;
            }
            cellEdit.Visible = true;
            cellEdit.BringToFront();
            cellEdit.Focus();
            cellEdit.SelectAll();
            list.Invalidate(ir);            // repaint the row with its cell blanked
        }

        void EndCellEdit(bool commit)
        {
            if (cellRow < 0) return;
            int row = cellRow;
            cellRow = -1;                   // reentrancy: hiding refires focus events
            cellEdit.Visible = false;
            list.Invalidate();              // uncover the blanked cell
            if (!commit) return;
            if (cellKind == Cell.Spot)
            {
                Point sp, ep;
                int pairs;
                if (ParseSpot(cellEdit.Text, out sp, out ep, out pairs))
                {
                    TakeRow r = take.Rows[row];
                    // one pair on a drag slides the whole gesture
                    if (r.Kind == RKind.Drag && pairs == 1)
                        ep = new Point(sp.X + (r.EX - r.X), sp.Y + (r.EY - r.Y));
                    take.SetCoords(row, sp.X, sp.Y, ep.X, ep.Y);
                    Touched(row);
                    SpotDot.Pop(sp.X, sp.Y);
                }
            }
            else
            {
                int v;
                if (int.TryParse(cellEdit.Text, out v) && v >= 0)
                {
                    if (cellKind == Cell.Len) take.SetLength(row, v);
                    else take.SetGap(row, v);
                    Touched(row);
                }
            }
            // Deliberately no focus change: the callers that end an edit on
            // purpose refocus what comes next; the LostFocus path must not
            // wrestle the focus back from wherever the user sent it
        }

        // The Tab walk: wait, length, position, then the next row's wait -
        // skipping cells a row doesn't have. Retiming never changes the row
        // structure, so the row index stays valid across the commit.
        void TabCell(bool back)
        {
            int row = cellRow;
            Cell kind = cellKind;
            EndCellEdit(true);
            if (row < 0 || row >= take.Rows.Count) { list.Focus(); return; }
            int dir = back ? -1 : 1;
            int k = (int)kind + dir;
            while (true)
            {
                if (k < 0) { row--; k = (int)Cell.Spot; }
                else if (k > (int)Cell.Spot) { row++; k = (int)Cell.Wait; }
                if (row < 0 || row >= take.Rows.Count) { list.Focus(); return; }
                if (HasCell(row, (Cell)k)) { BeginCellEdit(row, (Cell)k); return; }
                k += dir;
            }
        }

        // --- editing ----------------------------------------------------------

        void Touched(int keepNear)
        {
            RefreshRows(keepNear);
        }

        void Undo()
        {
            if (previewing) return;
            EndCellEdit(false);
            if (!take.Undo()) return;
            RefreshRows(Math.Max(0, list.SelectedIndex));
            if (!list.Focused) list.Focus();
        }

        void DeleteSelected()
        {
            var sel = SelRows();
            if (previewing || sel.Count == 0) return;
            int near = sel[0];
            take.DeleteRows(sel);
            Touched(near);
            if (!list.Focused) list.Focus();
        }

        // The same 3-2-1 capture the cards use for a fixed position: the
        // window steps aside, the countdown runs at the cursor, and the spot
        // under the pointer is handed to whatever asked for it.
        void CaptureSpot(Action<Point> done)
        {
            Point home = Location;
            Location = new Point(-4000, home.Y);
            var t = new Timer();
            int left = 3;
            t.Interval = 1000;
            t.Tick += delegate
            {
                if (--left > 0) { CursorToast.Pop("Capturing in " + left + "...", 1100, true); return; }
                t.Stop(); t.Dispose();
                Point p = Cursor.Position;
                Location = home;
                Activate();
                done(p);
            };
            CursorToast.Pop("Capturing in 3...", 1100, true);
            t.Start();
        }

        // --- adding steps -----------------------------------------------------

        static REv E(double ms, byte type, int a, int b)
        {
            var e = new REv();
            e.Ms = ms; e.Type = type; e.A = a; e.B = b;
            return e;
        }

        static REv[] ClickStep(Point p, int btn, bool dbl)
        {
            return dbl
                ? new REv[] { E(0, 0, p.X, p.Y), E(0, 1, btn, 0), E(40, 2, btn, 0),
                              E(90, 1, btn, 0), E(130, 2, btn, 0) }
                : new REv[] { E(0, 0, p.X, p.Y), E(0, 1, btn, 0), E(50, 2, btn, 0) };
        }

        void OpenAddMenu()
        {
            if (previewing) return;
            DropList.Open(addBtn, addBtn.Parent.RectangleToScreen(addBtn.Bounds),
                          StepNames, "", AddStep);
        }

        void AddStep(string what)
        {
            if (previewing) return;
            // After the last selected row; nothing selected appends at the end
            var sel = SelRows();
            int after = sel.Count > 0 ? sel[sel.Count - 1] : take.Rows.Count - 1;

            switch (what)
            {
                case "Left click":
                    CaptureSpot(delegate(Point p) { Insert(after, ClickStep(p, 0, false)); });
                    break;
                case "Double click":
                    CaptureSpot(delegate(Point p) { Insert(after, ClickStep(p, 0, true)); });
                    break;
                case "Right click":
                    CaptureSpot(delegate(Point p) { Insert(after, ClickStep(p, 1, false)); });
                    break;
                case "Middle click":
                    CaptureSpot(delegate(Point p) { Insert(after, ClickStep(p, 2, false)); });
                    break;
                case "Key press…":
                    using (var pr = new TextPrompt("Key press",
                        "Key to press (A, F5, Enter, Space…):", ""))
                    {
                        if (pr.ShowDialog(this) != DialogResult.OK) return;
                        ushort vk = HotkeyParser.KeyNameToVk(pr.Value.Trim());
                        if (vk == 0) { CursorToast.Pop("Unknown key name"); return; }
                        Insert(after, new REv[] { E(0, 3, vk, 0), E(50, 4, vk, 0) });
                    }
                    break;
                case "Scroll up":
                    Insert(after, new REv[] { E(0, 5, 120, 0) });
                    break;
                case "Scroll down":
                    Insert(after, new REv[] { E(0, 5, -120, 0) });
                    break;
                case "Move to…":
                    CaptureSpot(delegate(Point p) { Insert(after, new REv[] { E(0, 0, p.X, p.Y) }); });
                    break;
            }
        }

        void Insert(int after, REv[] step)
        {
            double at = take.InsertStep(after, step, 100);
            Touched(take.FindRowAt(at));
        }

        // --- preview ----------------------------------------------------------

        void Preview()
        {
            if (previewing) { Engine.Stop(previewId); return; }
            if (take.EventCount == 0) return;
            EndCellEdit(true);
            int from = list.SelectedIndices.Count > 0
                     ? take.Rows[list.SelectedIndices[0]].From : 0;
            string tmp = Path.Combine(Path.GetTempPath(),
                "polyclicker-preview-" + previewId + ".macro");
            try { take.WriteTo(tmp, from); }
            catch { return; }

            var pcfg = new SlotConfig();
            pcfg.Input = "Macro";
            pcfg.MacroLoop = false;     // once through, exactly like the card's
            pcfg.Interval = 0;          // loop-off path with no gap
            string warn;
            if (!Engine.StartMacro(previewId, pcfg, IntPtr.Zero, tmp, out warn)) return;
            previewing = true;
            playBtn.Kind = "stop";
            playBtn.Invalidate();
            // Off-screen, not hidden: the take must not click on the editor
            homeLoc = Location;
            Location = new Point(-4000, homeLoc.Y);
            CursorToast.Pop("Previewing take - plays once");
        }

        // --- file actions -----------------------------------------------------

        // True when the file now matches the buffer - including when it
        // already did
        bool SaveFile()
        {
            if (previewing) return false;
            EndCellEdit(true);
            if (!Dirty) { CursorToast.Pop("Already saved"); return true; }
            try
            {
                take.StampEdited();
                take.WriteTo(path, 0);
                savedPrint = take.Fingerprint();
                RefreshRows(Math.Max(0, list.SelectedIndex));
                CursorToast.Pop("Saved " + Path.GetFileName(path));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save it:\n\n" + ex.Message,
                    "Edit take", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void SaveCopy()
        {
            string oldBase = Path.GetFileNameWithoutExtension(path);
            using (var p = new TextPrompt("Save a copy", "Save these events as:", oldBase + " edit"))
            {
                if (p.ShowDialog(this) != DialogResult.OK) return;
                string name = MacroFile.Sanitize(p.Value);
                if (name.Length == 0) return;
                try
                {
                    take.StampEdited();
                    string dst = MacroFile.UniquePath(Path.GetDirectoryName(path), name);
                    take.WriteTo(dst, 0);
                    CursorToast.Pop("Saved " + Path.GetFileName(dst));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save it:\n\n" + ex.Message,
                        "Edit take", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // The name box commits like a card name: on Enter or on leaving the
        // field. A failed rename puts the old name back rather than leaving
        // the box lying about the file.
        void CommitName()
        {
            if (committingName) return;
            committingName = true;
            try
            {
                string oldBase = Path.GetFileNameWithoutExtension(path);
                string nb = MacroFile.Sanitize(nameBox.Text);
                if (nb.Length == 0 || nb == oldBase) { nameBox.Text = oldBase; return; }
                string dst = Path.Combine(Path.GetDirectoryName(path), nb + ".macro");
                if (File.Exists(dst))
                {
                    CursorToast.Pop("A macro called '" + nb + "' already exists");
                    nameBox.Text = oldBase;
                    return;
                }
                try { File.Move(path, dst); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't rename it:\n\n" + ex.Message,
                        "Rename macro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    nameBox.Text = oldBase;
                    return;
                }
                string oldName = Path.GetFileName(path);
                path = dst;
                nameBox.Text = nb;
                Text = nb + " - Edit take";
                CursorToast.Pop("Renamed to " + nb);
                if (onRenamed != null) onRenamed(oldName, nb + ".macro");
            }
            finally { committingName = false; }
        }

        // --- selection state --------------------------------------------------

        void OnSelection()
        {
            if (adjusting) return;
            // SelectedIndex is a plain read; the SelectedIndices collection
            // walks the control's item array and is the thing that throws
            // when this fires mid-mutation, so it stays behind a guard
            int first = list.SelectedIndex;
            delBtn.Enabled = upBtn.Enabled = downBtn.Enabled = first >= 0;
        }
    }
}
