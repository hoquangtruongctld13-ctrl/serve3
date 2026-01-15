import re
import threading
import queue
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from concurrent.futures import ThreadPoolExecutor, as_completed

from openai import OpenAI
from openpyxl import Workbook


MODEL_OPTIONS = [
    ("Gemini 3 Flash", "gemini-3-flash"),
    ("Gemini 3 Pro High", "gemini-3-pro-high"),
    ("Gemini 3 Pro Low", "gemini-3-pro-low"),
    ("Gemini 3 Pro (Image)", "gemini-3-pro-image"),
    ("Gemini 2.5 Flash", "gemini-2.5-flash"),
    ("Gemini 2.5 Flash Lite", "gemini-2.5-flash-lite"),
    ("Gemini 2.5 Pro", "gemini-2.5-pro"),
    ("Gemini 2.5 Flash (Thinking)", "gemini-2.5-flash-thinking"),
    ("Claude 4.5 Sonnet", "claude-sonnet-4-5"),
]

DEFAULT_BASE_URL = "http://34.56.197.96:8045/v1"
DEFAULT_API_KEY = "sk-antigravity"

DEFAULT_GUIDE_PROMPT = """Bạn là chuyên gia dịch phụ đề.
Nhiệm vụ: dịch sang TIẾNG VIỆT, tự nhiên, đúng ngữ cảnh, ngắn gọn (phù hợp phụ đề).
YÊU CẦU BẮT BUỘC:
- Trả đúng thứ tự.
- Mỗi phụ đề đúng 1 dòng.
- Định dạng chính xác: index: nội dung dịch
- Không thêm giải thích, không thêm dòng thừa, không đổi index.
"""


def parse_srt(text: str):
    """
    Parse SRT into list of dict:
    [
      {"index": 1, "time": "00:00:01,000 --> 00:00:02,000", "src": "Hello"},
      ...
    ]
    """
    # Normalize newlines
    text = text.replace("\r\n", "\n").replace("\r", "\n").strip()
    if not text:
        return []

    blocks = re.split(r"\n\s*\n", text)
    items = []
    for b in blocks:
        lines = [ln.strip("\n") for ln in b.split("\n") if ln.strip("\n") != ""]
        if len(lines) < 2:
            continue

        # Typical: index, time, text...
        # Some SRTs may omit index; handle safely
        idx = None
        time_line = None
        start_at = 0

        if re.match(r"^\d+$", lines[0].strip()):
            idx = int(lines[0].strip())
            start_at = 1

        # time line usually contains -->
        for i in range(start_at, min(start_at + 2, len(lines))):
            if "-->" in lines[i]:
                time_line = lines[i].strip()
                start_at = i + 1
                break

        if time_line is None:
            # Not a valid SRT block
            continue

        src_lines = lines[start_at:]
        src = " ".join([ln.strip() for ln in src_lines]).strip()

        if idx is None:
            idx = len(items) + 1

        items.append({"index": idx, "time": time_line, "src": src, "tr": ""})

    return items


def parse_tsv_like(text: str):
    """
    Parse Excel-like pasted text:
    - Prefer "index<TAB>text"
    - Also supports "index, text" (comma) if tab not found
    """
    text = text.replace("\r\n", "\n").replace("\r", "\n").strip()
    if not text:
        return []

    items = []
    lines = [ln for ln in text.split("\n") if ln.strip() != ""]
    auto_index = 1

    for ln in lines:
        if "\t" in ln:
            parts = ln.split("\t", 1)
        elif "," in ln:
            parts = ln.split(",", 1)
        else:
            parts = [ln]

        if len(parts) == 1:
            idx = auto_index
            src = parts[0].strip()
        else:
            left = parts[0].strip()
            right = parts[1].strip()
            if re.match(r"^\d+$", left):
                idx = int(left)
                src = right
            else:
                idx = auto_index
                src = ln.strip()

        items.append({"index": idx, "time": "", "src": src, "tr": ""})
        auto_index += 1

    return items


def detect_and_parse_subtitles(text: str):
    """
    Heuristic:
    - If contains '-->' and looks like SRT => parse_srt
    - Else parse_tsv_like
    """
    if "-->" in text:
        parsed = parse_srt(text)
        if parsed:
            return parsed, "srt"
    parsed = parse_tsv_like(text)
    return parsed, "tsv"


def build_user_payload(batch_items, guide_prompt: str):
    """
    Build user message:
    Provide list lines: "index\ttext"
    """
    lines = []
    for it in batch_items:
        idx = it["index"]
        src = it["src"].replace("\n", " ").strip()
        lines.append(f"{idx}\t{src}")

    payload = (
        "Hãy dịch các dòng phụ đề sau theo hướng dẫn.\n"
        "NHẮC LẠI format bắt buộc: index: nội dung dịch\n\n"
        "DANH SÁCH CẦN DỊCH:\n"
        + "\n".join(lines)
    )
    return payload


def parse_model_output(output: str):
    """
    Expect lines like:
    12: nội dung dịch
    Return dict {12: "..."}
    """
    mapping = {}
    output = output.replace("\r\n", "\n").replace("\r", "\n").strip()
    for ln in output.split("\n"):
        ln = ln.strip()
        if not ln:
            continue

        m = re.match(r"^(\d+)\s*:\s*(.*)$", ln)
        if m:
            idx = int(m.group(1))
            tr = m.group(2).strip()
            mapping[idx] = tr
    return mapping


class TreeviewCellEditor:
    """
    Enable editing a Treeview cell by double click.
    """

    def __init__(self, tree: ttk.Treeview):
        self.tree = tree
        self.entry = None
        self.tree.bind("<Double-1>", self._on_double_click)

    def _on_double_click(self, event):
        region = self.tree.identify("region", event.x, event.y)
        if region != "cell":
            return

        row_id = self.tree.identify_row(event.y)
        col_id = self.tree.identify_column(event.x)
        if not row_id or not col_id:
            return

        # Only allow editing Translation column (col #3)
        # columns: #1 Index, #2 Source, #3 Translation
        if col_id != "#3":
            return

        bbox = self.tree.bbox(row_id, col_id)
        if not bbox:
            return

        x, y, w, h = bbox
        value = self.tree.set(row_id, "Translation")

        self.entry = tk.Entry(self.tree)
        self.entry.insert(0, value)
        self.entry.select_range(0, tk.END)
        self.entry.focus_set()
        self.entry.place(x=x, y=y, width=w, height=h)

        def save_edit(_evt=None):
            new_val = self.entry.get()
            self.tree.set(row_id, "Translation", new_val)
            self.entry.destroy()
            self.entry = None

        def cancel_edit(_evt=None):
            self.entry.destroy()
            self.entry = None

        self.entry.bind("<Return>", save_edit)
        self.entry.bind("<FocusOut>", save_edit)
        self.entry.bind("<Escape>", cancel_edit)


class SubtitleTranslatorApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Subtitle Translator (Antigravity/OpenAI SDK Gateway)")
        self.geometry("1200x720")

        self.work_q = queue.Queue()
        self.items = []          # list of {"index","time","src","tr"}
        self.input_format = None # "srt" or "tsv"

        self._build_ui()
        self._poll_queue()

    def _build_ui(self):
        # Top config frame
        cfg = ttk.Frame(self, padding=10)
        cfg.pack(side=tk.TOP, fill=tk.X)

        ttk.Label(cfg, text="Base URL:").grid(row=0, column=0, sticky="w")
        self.base_url_var = tk.StringVar(value=DEFAULT_BASE_URL)
        ttk.Entry(cfg, textvariable=self.base_url_var, width=45).grid(row=0, column=1, sticky="w", padx=5)

        ttk.Label(cfg, text="API Key:").grid(row=0, column=2, sticky="w")
        self.api_key_var = tk.StringVar(value=DEFAULT_API_KEY)
        ttk.Entry(cfg, textvariable=self.api_key_var, width=35, show="*").grid(row=0, column=3, sticky="w", padx=5)

        ttk.Label(cfg, text="Model:").grid(row=0, column=4, sticky="w")
        self.model_var = tk.StringVar(value=MODEL_OPTIONS[0][0])
        model_names = [m[0] for m in MODEL_OPTIONS]
        ttk.Combobox(cfg, textvariable=self.model_var, values=model_names, width=22, state="readonly").grid(row=0, column=5, sticky="w", padx=5)

        ttk.Label(cfg, text="Số dòng/batch:").grid(row=0, column=6, sticky="w")
        self.batch_size_var = tk.StringVar(value="100")
        ttk.Entry(cfg, textvariable=self.batch_size_var, width=6).grid(row=0, column=7, sticky="w", padx=5)

        ttk.Label(cfg, text="Song song:").grid(row=0, column=8, sticky="w")
        self.concurrency_var = tk.StringVar(value="3")
        ttk.Entry(cfg, textvariable=self.concurrency_var, width=4).grid(row=0, column=9, sticky="w", padx=5)

        ttk.Button(cfg, text="Test kết nối", command=self.on_test_connection).grid(row=0, column=10, padx=5)


        # Prompt frame
        prompt_fr = ttk.LabelFrame(self, text="Prompt hướng dẫn dịch", padding=10)
        prompt_fr.pack(side=tk.TOP, fill=tk.X, padx=10, pady=(0, 10))

        self.guide_text = tk.Text(prompt_fr, height=6, wrap="word")
        self.guide_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        self.guide_text.insert("1.0", DEFAULT_GUIDE_PROMPT)

        # Input + Buttons frame
        mid = ttk.Frame(self, padding=10)
        mid.pack(side=tk.TOP, fill=tk.BOTH, expand=False)

        left = ttk.LabelFrame(mid, text="Input phụ đề (dán SRT hoặc Excel TSV: index<TAB>text)", padding=10)
        left.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        self.input_text = tk.Text(left, height=10, wrap="none")
        self.input_text.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        btns = ttk.Frame(left)
        btns.pack(side=tk.TOP, fill=tk.X, pady=(8, 0))

        ttk.Button(btns, text="Parse -> Bảng", command=self.on_parse_to_table).pack(side=tk.LEFT)
        ttk.Button(btns, text="Load SRT", command=self.on_load_srt).pack(side=tk.LEFT, padx=5)
        ttk.Button(btns, text="Load TXT/TSV", command=self.on_load_txt).pack(side=tk.LEFT, padx=5)
        ttk.Button(btns, text="Clear", command=self.on_clear_all).pack(side=tk.LEFT, padx=5)

        right = ttk.LabelFrame(mid, text="Điều khiển", padding=10)
        right.pack(side=tk.LEFT, fill=tk.Y, padx=(10, 0))

        ttk.Button(right, text="Dịch (Translate)", command=self.on_translate).pack(side=tk.TOP, fill=tk.X)
        ttk.Button(right, text="Dừng (Stop)", command=self.on_stop).pack(side=tk.TOP, fill=tk.X, pady=(5, 0))
        ttk.Separator(right, orient="horizontal").pack(side=tk.TOP, fill=tk.X, pady=10)

        ttk.Button(right, text="Xuất SRT", command=self.on_export_srt).pack(side=tk.TOP, fill=tk.X)
        ttk.Button(right, text="Xuất Excel (.xlsx)", command=self.on_export_xlsx).pack(side=tk.TOP, fill=tk.X, pady=(5, 0))

        ttk.Separator(right, orient="horizontal").pack(side=tk.TOP, fill=tk.X, pady=10)
        self.status_var = tk.StringVar(value="Ready.")
        ttk.Label(right, textvariable=self.status_var, wraplength=220).pack(side=tk.TOP, fill=tk.X)

        # Table frame
        table_fr = ttk.LabelFrame(self, text="Bảng phụ đề (double click cột Translation để sửa)", padding=10)
        table_fr.pack(side=tk.TOP, fill=tk.BOTH, expand=True, padx=10, pady=(0, 10))

        cols = ("Index", "Source", "Translation")
        self.tree = ttk.Treeview(table_fr, columns=cols, show="headings", height=14)
        self.tree.heading("Index", text="Index")
        self.tree.heading("Source", text="Source")
        self.tree.heading("Translation", text="Translation")

        self.tree.column("Index", width=80, anchor="center")
        self.tree.column("Source", width=520, anchor="w")
        self.tree.column("Translation", width=520, anchor="w")

        yscroll = ttk.Scrollbar(table_fr, orient="vertical", command=self.tree.yview)
        xscroll = ttk.Scrollbar(table_fr, orient="horizontal", command=self.tree.xview)
        self.tree.configure(yscrollcommand=yscroll.set, xscrollcommand=xscroll.set)

        self.tree.pack(side=tk.TOP, fill=tk.BOTH, expand=True)
        yscroll.pack(side=tk.RIGHT, fill=tk.Y)
        xscroll.pack(side=tk.BOTTOM, fill=tk.X)

        TreeviewCellEditor(self.tree)

        # Log
        log_fr = ttk.LabelFrame(self, text="Log", padding=10)
        log_fr.pack(side=tk.BOTTOM, fill=tk.X, padx=10, pady=(0, 10))

        self.log_text = tk.Text(log_fr, height=6, wrap="word")
        self.log_text.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        self.stop_flag = threading.Event()
        self.worker_thread = None

    def log(self, msg: str):
        self.log_text.insert(tk.END, msg + "\n")
        self.log_text.see(tk.END)

    def set_status(self, msg: str):
        self.status_var.set(msg)

    def _poll_queue(self):
        try:
            while True:
                item = self.work_q.get_nowait()
                kind = item.get("kind")

                if kind == "status":
                    self.set_status(item["msg"])

                elif kind == "log":
                    self.log(item["msg"])

                elif kind == "update_translations":
                    mapping = item["mapping"]
                    self._apply_translation_mapping(mapping)

                elif kind == "done":
                    self.set_status("Done.")
                    self.log("Hoàn tất dịch.")
                    self.worker_thread = None
                    self.stop_flag.clear()

                elif kind == "error":
                    self.set_status("Error.")
                    self.log("Lỗi: " + item["msg"])
                    messagebox.showerror("Lỗi", item["msg"])
                    self.worker_thread = None
                    self.stop_flag.clear()

        except queue.Empty:
            pass

        self.after(120, self._poll_queue)

    def _get_model_id(self):
        name = self.model_var.get().strip()
        for n, mid in MODEL_OPTIONS:
            if n == name:
                return mid
        return MODEL_OPTIONS[0][1]

    def on_test_connection(self):
        base_url = self.base_url_var.get().strip()
        api_key = self.api_key_var.get().strip()
        model_id = self._get_model_id()
        if not base_url or not api_key:
            messagebox.showwarning("Thiếu thông tin", "Bạn cần nhập Base URL và API Key.")
            return

        def _test():
            try:
                self.work_q.put({"kind": "status", "msg": "Đang test kết nối..."})
                client = OpenAI(base_url=base_url, api_key=api_key)
                resp = client.chat.completions.create(
                    model=model_id,
                    messages=[{"role": "user", "content": "ping"}],
                    temperature=0.0,
                )
                out = resp.choices[0].message.content
                self.work_q.put({"kind": "log", "msg": f"Test OK. Model trả về: {out!r}"})
                self.work_q.put({"kind": "status", "msg": "Ready."})
            except Exception as e:
                self.work_q.put({"kind": "error", "msg": str(e)})

        threading.Thread(target=_test, daemon=True).start()

    def on_load_srt(self):
        path = filedialog.askopenfilename(
            title="Chọn file SRT",
            filetypes=[("SRT files", "*.srt"), ("All files", "*.*")]
        )
        if not path:
            return
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                content = f.read()
            self.input_text.delete("1.0", tk.END)
            self.input_text.insert("1.0", content)
            self.set_status(f"Loaded: {path}")
        except Exception as e:
            messagebox.showerror("Lỗi", str(e))

    def on_load_txt(self):
        path = filedialog.askopenfilename(
            title="Chọn file TXT/TSV",
            filetypes=[("Text files", "*.txt *.tsv *.csv"), ("All files", "*.*")]
        )
        if not path:
            return
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                content = f.read()
            self.input_text.delete("1.0", tk.END)
            self.input_text.insert("1.0", content)
            self.set_status(f"Loaded: {path}")
        except Exception as e:
            messagebox.showerror("Lỗi", str(e))

    def on_clear_all(self):
        self.input_text.delete("1.0", tk.END)
        self.tree.delete(*self.tree.get_children())
        self.items = []
        self.input_format = None
        self.set_status("Ready.")
        self.log("Đã clear.")

    def on_parse_to_table(self):
        raw = self.input_text.get("1.0", tk.END)
        items, fmt = detect_and_parse_subtitles(raw)
        if not items:
            messagebox.showwarning("Không parse được", "Không tìm thấy dữ liệu phụ đề hợp lệ.")
            return

        # Sort by index just in case
        items.sort(key=lambda x: x["index"])
        self.items = items
        self.input_format = fmt

        self._refresh_table()
        self.set_status(f"Parsed {len(items)} dòng ({fmt}).")
        self.log(f"Parse OK: {len(items)} dòng ({fmt}).")

    def _refresh_table(self):
        self.tree.delete(*self.tree.get_children())
        for it in self.items:
            self.tree.insert("", tk.END, values=(it["index"], it["src"], it.get("tr", "")))

    def _apply_translation_mapping(self, mapping: dict):
        # Update internal items
        idx_to_item = {it["index"]: it for it in self.items}
        for idx, tr in mapping.items():
            if idx in idx_to_item:
                idx_to_item[idx]["tr"] = tr

        # Update table rows
        # Tree rows correspond to current order in self.items
        for row_id in self.tree.get_children():
            vals = self.tree.item(row_id, "values")
            if not vals:
                continue
            try:
                idx = int(vals[0])
            except Exception:
                continue
            if idx in mapping:
                self.tree.set(row_id, "Translation", mapping[idx])

    def on_stop(self):
        if self.worker_thread and self.worker_thread.is_alive():
            self.stop_flag.set()
            self.set_status("Đang dừng...")
            self.log("Đã gửi tín hiệu dừng. Sẽ dừng sau batch hiện tại.")
        else:
            self.set_status("Ready.")

    def on_translate(self):
        if not self.items:
            messagebox.showwarning("Chưa có dữ liệu", "Bạn cần Parse phụ đề -> Bảng trước.")
            return
        if self.worker_thread and self.worker_thread.is_alive():
            messagebox.showinfo("Đang chạy", "Đang dịch rồi. Nếu muốn dừng, bấm Stop.")
            return

        # Sync any manual edits from Treeview back to self.items
        self._sync_table_to_items()

        try:
            batch_size = int(self.batch_size_var.get().strip())
            if batch_size <= 0:
                raise ValueError
        except Exception:
            messagebox.showwarning("Sai batch", "Số dòng/batch phải là số nguyên > 0.")
            return

        try:
            concurrency = int(self.concurrency_var.get().strip())
            if concurrency <= 0:
                raise ValueError
        except Exception:
            messagebox.showwarning("Sai song song", "Song song phải là số nguyên > 0 (mặc định 3).")
            return

        base_url = self.base_url_var.get().strip()
        api_key = self.api_key_var.get().strip()
        model_id = self._get_model_id()
        guide = self.guide_text.get("1.0", tk.END).strip()

        if not base_url or not api_key:
            messagebox.showwarning("Thiếu thông tin", "Bạn cần nhập Base URL và API Key.")
            return
        if not guide:
            messagebox.showwarning("Thiếu prompt", "Bạn cần nhập Prompt hướng dẫn dịch.")
            return

        self.stop_flag.clear()
        self.worker_thread = threading.Thread(
            target=self._translate_worker,
            args=(base_url, api_key, model_id, guide, batch_size, concurrency),
            daemon=True,
        )
        self.worker_thread.start()

    def _sync_table_to_items(self):
        # Pull translation edits from table back into self.items
        idx_to_item = {it["index"]: it for it in self.items}
        for row_id in self.tree.get_children():
            vals = self.tree.item(row_id, "values")
            if not vals:
                continue
            try:
                idx = int(vals[0])
            except Exception:
                continue
            tr = vals[2] if len(vals) > 2 else ""
            if idx in idx_to_item:
                idx_to_item[idx]["tr"] = tr

    def _translate_worker(self, base_url, api_key, model_id, guide_prompt, batch_size, concurrency):
        try:
            if "image" in model_id.lower():
                self.work_q.put({"kind": "log", "msg": "Cảnh báo: bạn đang chọn model Image. Dịch phụ đề thường nên dùng model text."})

            client = OpenAI(base_url=base_url, api_key=api_key)

            total = len(self.items)
            self.work_q.put({"kind": "status", "msg": f"Bắt đầu dịch {total} dòng, batch={batch_size}, song song={concurrency}, model={model_id}..."})
            self.work_q.put({"kind": "log", "msg": f"Model: {model_id}, total: {total}, batch: {batch_size}, concurrency: {concurrency}"})

            pending = [it for it in self.items if not it.get("tr", "").strip()]
            if not pending:
                self.work_q.put({"kind": "log", "msg": "Không có dòng nào cần dịch (Translation đã có)."})
                self.work_q.put({"kind": "done"})
                return

            pending.sort(key=lambda x: x["index"])

            # Chia thành các batch
            batches = []
            for start in range(0, len(pending), batch_size):
                batch = pending[start:start + batch_size]
                batches.append(batch)

            total_batches = len(batches)
            self.work_q.put({"kind": "log", "msg": f"Tổng batch cần dịch: {total_batches}"})

            # Hàm gọi API cho 1 batch
            def translate_one_batch(batch_items):
                if self.stop_flag.is_set():
                    return {"mapping": {}, "range": None, "raw": None, "skipped": True}

                batch_idx_first = batch_items[0]["index"]
                batch_idx_last = batch_items[-1]["index"]

                user_payload = build_user_payload(batch_items, guide_prompt)
                messages = [
                    {"role": "system", "content": guide_prompt},
                    {"role": "user", "content": user_payload},
                ]

                resp = client.chat.completions.create(
                    model=model_id,
                    messages=messages,
                    temperature=0.2,
                )

                out = resp.choices[0].message.content or ""
                mapping = parse_model_output(out)
                return {
                    "mapping": mapping,
                    "range": (batch_idx_first, batch_idx_last),
                    "raw": out,
                    "skipped": False,
                }

            completed = 0

            # Chạy song song
            with ThreadPoolExecutor(max_workers=concurrency) as executor:
                future_to_batch = {executor.submit(translate_one_batch, b): b for b in batches}

                for fut in as_completed(future_to_batch):
                    if self.stop_flag.is_set():
                        self.work_q.put({"kind": "status", "msg": "Đang dừng... (chờ các batch đang chạy kết thúc)"})
                        # Không huỷ cứng vì nhiều gateway không thích abort giữa chừng; cứ để batch đang chạy xong.
                        # (Nếu muốn huỷ mạnh, có thể attempt fut.cancel() với futures chưa chạy.)
                    try:
                        res = fut.result()
                    except Exception as e:
                        raise RuntimeError(f"Lỗi batch: {e}")

                    if res.get("skipped"):
                        continue

                    rng = res.get("range")
                    mapping = res.get("mapping") or {}
                    raw = res.get("raw") or ""

                    if not mapping:
                        self.work_q.put({"kind": "log", "msg": f"Batch {rng} trả về không đúng format. Raw output:"})
                        self.work_q.put({"kind": "log", "msg": raw})
                        raise RuntimeError("Model output không parse được. Hãy siết prompt hoặc giảm batch size hoặc giảm song song.")

                    # Update UI
                    self.work_q.put({"kind": "update_translations", "mapping": mapping})

                    completed += 1
                    if rng:
                        self.work_q.put({"kind": "log", "msg": f"Batch OK {rng[0]}..{rng[1]} (parsed {len(mapping)} lines) [{completed}/{total_batches}]"})

                    self.work_q.put({"kind": "status", "msg": f"Tiến độ: {completed}/{total_batches} batch xong (song song={concurrency})"})

            if self.stop_flag.is_set():
                self.work_q.put({"kind": "log", "msg": "Dừng theo yêu cầu. Các batch đã hoàn thành được giữ lại."})
                self.work_q.put({"kind": "status", "msg": "Đã dừng."})

            self.work_q.put({"kind": "done"})

        except Exception as e:
            self.work_q.put({"kind": "error", "msg": str(e)})


    def on_export_srt(self):
        if not self.items:
            messagebox.showwarning("Chưa có dữ liệu", "Không có dữ liệu để xuất.")
            return

        # Sync table edits
        self._sync_table_to_items()

        path = filedialog.asksaveasfilename(
            title="Lưu SRT",
            defaultextension=".srt",
            filetypes=[("SRT files", "*.srt")]
        )
        if not path:
            return

        # If input was SRT, keep original time; else generate dummy time
        lines = []
        for i, it in enumerate(sorted(self.items, key=lambda x: x["index"]), start=1):
            idx = it["index"]
            time_line = it["time"] if it.get("time") else "00:00:00,000 --> 00:00:00,000"
            text = it.get("tr", "").strip() or it.get("src", "").strip()

            lines.append(str(idx))
            lines.append(time_line)
            lines.append(text)
            lines.append("")

        try:
            with open(path, "w", encoding="utf-8") as f:
                f.write("\n".join(lines).strip() + "\n")
            self.log(f"Đã xuất SRT: {path}")
            self.set_status(f"Exported SRT: {path}")
        except Exception as e:
            messagebox.showerror("Lỗi", str(e))

    def on_export_xlsx(self):
        if not self.items:
            messagebox.showwarning("Chưa có dữ liệu", "Không có dữ liệu để xuất.")
            return

        # Sync table edits
        self._sync_table_to_items()

        path = filedialog.asksaveasfilename(
            title="Lưu Excel",
            defaultextension=".xlsx",
            filetypes=[("Excel files", "*.xlsx")]
        )
        if not path:
            return

        try:
            wb = Workbook()
            ws = wb.active
            ws.title = "Subtitles"
            ws.append(["Index", "Source", "Translation", "Time"])

            for it in sorted(self.items, key=lambda x: x["index"]):
                ws.append([it["index"], it["src"], it.get("tr", ""), it.get("time", "")])

            wb.save(path)
            self.log(f"Đã xuất Excel: {path}")
            self.set_status(f"Exported Excel: {path}")
        except Exception as e:
            messagebox.showerror("Lỗi", str(e))


if __name__ == "__main__":
    app = SubtitleTranslatorApp()
    app.mainloop()
