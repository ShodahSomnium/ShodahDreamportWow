using System;
using System.Threading;
using SomniumSpace.Worlds.SomniumWoW.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumWoW.Game
{
    /// <summary>
    /// The room's in-world FOLDER BROWSER for the World of Warcraft 3.3.5a install ("Choose your World of Warcraft 3.3.5a
    /// folder - the one with Wow.exe"): an overlay over the room panel in the room's own look (backdrop, glue buttons; the
    /// mouse on desktop, the ray in VR). Drives (the ready fixed and removable ones), then folders; Up; pages of 14 rows.
    /// A folder WowInstall accepts is marked ("- WoW 3.3.5a", blue) and "Use this folder" takes it; a Data folder gives the
    /// install above it, a folder with exactly one install inside gives that one (both shown in words). Desktop also has
    /// a text field to paste a path (Enter or Go). EVERY listing and check runs on a worker thread (WowInstall.ListDrives /
    /// List): the frame only draws results, so a slow or dead network drive never stalls it; a result for a folder the
    /// player has already left is dropped. Errors (access denied, drive not ready) are shown in plain words.
    /// UI only: WowGame owns the boot and the saving (Chosen / Cancelled). The overlay is a child of the room panel, so a
    /// panel rebuild takes it away: it is built again on the new panel while the browser is open.
    /// </summary>
    public sealed class WowInstallPicker : MonoBehaviour
    {
        /// <summary>The box on the room panel (panel units from the top-left): under the logo, over the tabs and the status line.</summary>
        public const float BoxX = 30f, BoxY = 238f, BoxW = 1040f, BoxH = 596f;

        public const string Title = "Choose your World of Warcraft 3.3.5a folder - the one with Wow.exe";

        private const int Rows = 7, Columns = 2, PerPage = Rows * Columns;
        private const float ListTop = 140f, RowPitch = 44f, RowHeight = 40f, ColumnWidth = 475f, ColumnPitch = 485f;
        private const float SlowAfter = 2.5f;

        private static readonly Color Green = new Color(0.35f, 0.95f, 0.35f);
        private static readonly Color Orange = new Color(1f, 0.62f, 0.2f);
        private static readonly Color Red = new Color(1f, 0.3f, 0.25f);

        /// <summary>"Use this folder": the install root (checked on the worker a moment ago).</summary>
        public event Action<string> Chosen;

        /// <summary>Cancel pressed (only offered when a game is already loaded).</summary>
        public event Action Cancelled;

        private WowRoom _room;
        private GameObject _overlay;
        private RectTransform _list;
        private TextMeshProUGUI _where, _pageLabel, _found, _message;
        private WowGlueButton _up, _prev, _next, _use;
        private TMP_InputField _field;
        private string _typed = "";
        private bool _open, _canCancel, _builtForXr;

        // What is shown.
        private WowInstall.FolderListing _listing;
        private string _asked;                 // the folder asked last (null = the drive list)
        private int _page;
        private string _message0 = "";         // the opener's message (why the browser is open)
        private string _note;                  // a message set by WowGame (e.g. the chosen folder failed)
        private bool _noteError;

        // The worker (one short thread per listing; a stuck one is simply left behind).
        private readonly object _lock = new object();
        private int _job;                                  // main thread
        private int _doneJob = -1;                         // under _lock
        private WowInstall.FolderListing _done;            // under _lock
        private bool _busy, _slowShown;
        private float _busySince;

        public bool IsOpen { get { return _open; } }

        public static WowInstallPicker Create(WowRoom room)
        {
            var p = room.gameObject.AddComponent<WowInstallPicker>();
            p._room = room;
            return p;
        }

        /// <summary>
        /// Opens the browser at start (a folder; one that is gone shows its nearest existing parent; null = the drive list),
        /// with message under it (why it is open). canCancel: a Cancel button (a game is loaded; without one the browser
        /// stays until a folder works).
        /// </summary>
        public void Open(string start, string message, bool canCancel, string why)
        {
            _open = true;
            _canCancel = canCancel;
            _message0 = message ?? "";
            _note = null;
            _listing = null;
            _page = 0;
            Build();
            Request(start, true);
            Show();
            WowLog.Info("Install: folder browser opened (" + why + ") at " + (start ?? "the drive list") + (canCancel ? "" : " (no Cancel: no game loaded)") + ".");
        }

        /// <summary>Closes it (a pending listing's result is dropped).</summary>
        public void Close()
        {
            if (!_open && _overlay == null) return;
            _open = false;
            _job++;
            _busy = false;

            if (_overlay != null)
            {
                Destroy(_overlay);
                _overlay = null;
            }
        }

        /// <summary>A line under the list from WowGame (error = red); replaces the opener's message until the next folder.</summary>
        public void SetMessage(string text, bool error)
        {
            _note = text;
            _noteError = error;
            ShowMessage();
        }

        // ------------------------------------------------------------------ the worker

        private void Request(string folder, bool walkUp)
        {
            int id = ++_job;
            _asked = folder;
            _busy = true;
            _slowShown = false;
            _busySince = Time.realtimeSinceStartup;
            _note = null;
            ShowMessage();

            var t = new Thread(() =>
            {
                WowInstall.FolderListing r;

                try
                {
                    r = folder == null ? WowInstall.ListDrives() : WowInstall.List(folder, walkUp);
                }
                catch (Exception e)
                {
                    r = new WowInstall.FolderListing { Folder = folder, Parent = folder != null ? WowInstall.ParentOf(folder) : null, Error = WowInstall.Plain(e) };
                }

                lock (_lock)
                {
                    if (id > _doneJob)
                    {
                        _doneJob = id;
                        _done = r;
                    }
                }
            })
            { IsBackground = true, Name = "WoW folder browser" };

            try
            {
                t.Start();
            }
            catch (Exception e)
            {
                _busy = false;
                SetMessage("The folder could not be read: " + e.Message, true);
            }
        }

        private void Update()
        {
            if (!_open)
            {
                return;
            }

            if (_overlay == null || _builtForXr != WowPlayer.XrPresent)
            {
                Build();   // the room panel was rebuilt (the overlay went with it), or desktop <-> headset
                Show();
            }
            else if (_overlay.transform.parent != null && _overlay.transform.GetSiblingIndex() != _overlay.transform.parent.childCount - 1)
            {
                _overlay.transform.SetAsLastSibling();   // something was added to the panel later: stay on top
            }

            WowInstall.FolderListing r = null;

            lock (_lock)
            {
                if (_done != null && _doneJob == _job)
                {
                    r = _done;
                    _done = null;
                }
            }

            if (r != null)
            {
                _busy = false;
                _listing = r;
                _page = 0;
                Show();
                WowLog.Info("Install: browser - " + (r.Folder ?? "the drive list") + ": " + r.Entries.Count + " of " + r.Total + " folders, " + r.Ms + " ms"
                    + (r.Install != null ? ", install " + r.Install : "") + (r.Error != null ? ", problem: " + r.Error : "") + ".");
            }
            else if (_busy && !_slowShown && Time.realtimeSinceStartup - _busySince > SlowAfter)
            {
                _slowShown = true;
                ShowMessage();
            }
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            if (_overlay != null)
            {
                Destroy(_overlay);
                _overlay = null;
            }

            _list = null;
            _where = _pageLabel = _found = _message = null;
            _up = _prev = _next = _use = null;
            _field = null;
            _builtForXr = WowPlayer.XrPresent;

            if (_room == null || _room.Panel == null)
            {
                return;
            }

            RectTransform blocker = WowUi.Place(_room.Panel.transform, "Install Picker", 0f, 0f, WowRoom.PanelWidth, WowRoom.PanelHeight);
            _overlay = blocker.gameObject;
            var dim = _overlay.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = true;   // nothing under the browser can be pressed

            RectTransform box = WowUi.Place(blocker, "Box", BoxX, BoxY, BoxW, BoxH);
            // WoW's dialog background tile is see-through: an opaque plate behind it (the Settings dialog's recipe).
            RawImage plate = WowUi.Picture(box, "Plate", null, new Rect(0f, 0f, 1f, 1f), 6f, 6f, BoxW - 12f, BoxH - 12f);
            plate.color = new Color(0.02f, 0.02f, 0.025f, 1f);
            WowUi.Backdrop(box, 0f, 0f, BoxW, BoxH, 32f);

            TextMeshProUGUI title = WowUi.Text(box, "Title", Title, 23f, WowUi.GlueGold, TextAlignmentOptions.Center, 40f, 20f, BoxW - 80f, 32f);
            title.enableAutoSizing = true;
            title.fontSizeMin = 15f;
            title.fontSizeMax = 23f;

            _where = WowUi.Text(box, "Where", "", 18f, Color.white, TextAlignmentOptions.Left, 40f, 56f, BoxW - 80f, 28f);
            _where.textWrappingMode = TextWrappingModes.NoWrap;

            WowUi.Button(box, "Drives", 40f, 90f, 130f, 42f, 19f, () => Request(null, false));
            _up = WowUi.Button(box, "Up", 176f, 90f, 110f, 42f, 19f, GoUp);

            if (!_builtForXr)
            {
                // Desktop: paste a path (Ctrl+V), Enter or Go. The headset uses the ray and the list only.
                _field = WowTextField.Create(box, "Install Path", 296f, 90f, 470f, 42f, 18f, @"or paste a folder path, e.g. E:\Games\World of Warcraft 3.3.5a", false, 400);
                _field.text = _typed;
                _field.onValueChanged.AddListener(v => _typed = v ?? "");
                _field.onSubmit.AddListener(_ => GoTyped());
                WowUi.Button(box, "Go", 772f, 90f, 90f, 42f, 19f, GoTyped);
            }

            if (_canCancel)
            {
                WowUi.Button(box, "Cancel", 870f, 90f, 130f, 42f, 19f, Cancel);
            }

            _list = WowUi.Place(box, "List", 0f, 0f, BoxW, BoxH);

            _prev = WowUi.Button(box, "< Previous", 40f, 454f, 170f, 38f, 18f, () => Turn(-1));
            _pageLabel = WowUi.Text(box, "Page", "", 17f, WowUi.Grey, TextAlignmentOptions.Center, 220f, 458f, 600f, 30f);
            _next = WowUi.Button(box, "Next >", 830f, 454f, 170f, 38f, 18f, () => Turn(1));

            _found = WowUi.Text(box, "Found", "", 19f, Green, TextAlignmentOptions.MidlineLeft, 40f, 500f, 690f, 44f);
            _found.enableAutoSizing = true;
            _found.fontSizeMin = 12f;
            _found.fontSizeMax = 19f;
            _use = WowUi.Button(box, "Use this folder", 740f, 500f, 260f, 44f, 20f, Use);
            _use.gameObject.SetActive(false);   // Show() decides

            _message = WowUi.Text(box, "Message", "", 17f, WowUi.Grey, TextAlignmentOptions.MidlineLeft, 40f, 548f, BoxW - 80f, 36f);
            _message.enableAutoSizing = true;
            _message.fontSizeMin = 11f;
            _message.fontSizeMax = 17f;

            _overlay.transform.SetAsLastSibling();
        }

        /// <summary>Draws the current listing (rows of this page, paging, the found line, the message).</summary>
        private void Show()
        {
            if (_overlay == null || _list == null)
            {
                return;
            }

            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                Destroy(_list.GetChild(i).gameObject);
            }

            WowInstall.FolderListing l = _listing;
            _where.text = _busy && l == null ? "Looking " + (_asked == null ? "at the drives of this PC..." : "in " + _asked + "...")
                : l == null ? "" : l.Folder == null ? "Looking at: the drives of this PC" : "Looking in: " + l.Folder;
            int n = l != null ? l.Entries.Count : 0;
            int pages = Math.Max(1, (n + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);

            for (int k = 0; k < PerPage; k++)
            {
                int i = _page * PerPage + k;
                if (i >= n) break;
                WowInstall.FolderEntry e = l.Entries[i];
                int col = k / Rows, row = k % Rows;   // down the first column, then the second
                string path = e.Path;
                string label = e.IsInstall ? e.Name + "   - WoW 3.3.5a" : e.Name;
                WowGlueButton b = WowUi.Button(_list, label, 40f + col * ColumnPitch, ListTop + row * RowPitch, ColumnWidth, RowHeight, 17f, () => Request(path, false));
                b.Selected = e.IsInstall;
            }

            if (_prev != null) _prev.Interactable = _page > 0;
            if (_next != null) _next.Interactable = _page < pages - 1;
            if (_up != null) _up.Interactable = l != null && l.Folder != null;

            if (l == null)
            {
                _pageLabel.text = "";
            }
            else if (n == 0)
            {
                _pageLabel.text = l.Error != null ? "" : l.Folder == null ? "No drives." : "No folders in here.";
            }
            else
            {
                _pageLabel.text = "Page " + (_page + 1) + " of " + pages + "  -  " + (l.Folder == null ? n + (n == 1 ? " drive" : " drives") : n + (n == 1 ? " folder" : " folders"))
                    + (l.Total > n ? " (the first " + n + " of " + l.Total + ")" : "");
            }

            if (l != null && l.Install != null)
            {
                _found.text = l.InstallText ?? ("World of Warcraft 3.3.5a: " + l.Install);
                _found.color = Green;
                _use.gameObject.SetActive(true);
                _use.SetLabel(WowInstall.SameFolder(l.Install, l.Folder) ? "Use this folder" : "Use that folder");
                _use.Interactable = true;
            }
            else
            {
                _found.text = l == null ? ""
                    : l.Note ?? (l.Folder == null ? "Pick the drive World of Warcraft 3.3.5a is on." : "No World of Warcraft 3.3.5a here - open a folder, or go Up.");
                _found.color = l != null && l.Note != null ? Orange : WowUi.Grey;
                _use.gameObject.SetActive(false);
            }

            ShowMessage();
        }

        private void ShowMessage()
        {
            if (_message == null)
            {
                return;
            }

            string text;
            bool error = false;

            if (_note != null)
            {
                text = _note;
                error = _noteError;
            }
            else if (_busy)
            {
                string what = _asked == null ? "the drives" : _asked;
                text = _slowShown ? "Still reading " + what + " - the drive is slow or not answering. You can pick another folder meanwhile." : "Reading " + what + "...";
            }
            else if (_listing != null && _listing.Error != null)
            {
                text = _listing.Error;
                error = true;
            }
            else if (_listing != null && _listing.Moved != null)
            {
                text = _listing.Moved;
            }
            else
            {
                text = _message0;
            }

            _message.text = text ?? "";
            _message.color = error ? Red : (_note != null ? Color.white : WowUi.Grey);

            if (_busy && _use != null)
            {
                _use.Interactable = false;   // the found line belongs to the folder before
            }
        }

        // ------------------------------------------------------------------ buttons

        private void GoUp()
        {
            if (_listing == null || _listing.Folder == null)
            {
                return;
            }

            Request(_listing.Parent, false);   // null at a drive root: the drive list
        }

        private void GoTyped()
        {
            string p = WowInstall.CleanPath(_field != null ? _field.text : _typed);

            if (p == null)
            {
                SetMessage(@"Type or paste a folder path first (e.g. E:\Games\World of Warcraft 3.3.5a).", true);
                return;
            }

            WowLog.Info("Install: browser - path typed: " + p + ".");
            Request(p, false);
        }

        private void Turn(int d)
        {
            _page += d;
            Show();
        }

        private void Use()
        {
            if (_busy || _listing == null || _listing.Install == null)
            {
                return;
            }

            string root = _listing.Install;
            WowLog.Info("Install: folder chosen in the browser: " + root + (WowInstall.SameFolder(root, _listing.Folder) ? "" : " (from " + _listing.Folder + ")") + ".");
            var h = Chosen;

            if (h != null)
            {
                h(root);
            }
        }

        private void Cancel()
        {
            WowLog.Info("Install: folder browser closed (Cancel).");
            Close();
            var h = Cancelled;

            if (h != null)
            {
                h();
            }
        }

        private void OnDestroy()
        {
            _open = false;
            _job++;
        }
    }
}
