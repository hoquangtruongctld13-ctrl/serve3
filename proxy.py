#!/usr/bin/env python3
"""
KiotProxy Manager - Ứng dụng quản lý Proxy từ KiotProxy API
Dựa trên tài liệu API chính thức của KiotProxy

Chức năng:
- Lấy proxy mới / Đổi proxy
- Xem thông tin proxy hiện tại
- Thoát proxy khỏi key
- Test proxy
- Hiển thị danh sách proxy đã sử dụng
"""

import tkinter as tk
from tkinter import ttk, messagebox, scrolledtext
import requests
import threading
import json
import time
from datetime import datetime, timedelta
from concurrent.futures import ThreadPoolExecutor, as_completed
import webbrowser


class KiotProxyApp:
    """Main application class for KiotProxy Manager"""
    
    # API Configuration
    BASE_URL = "https://api.kiotproxy.com/api/v1/proxies"
    
    # Region options
    REGIONS = {
        "random": "🎲 Ngẫu nhiên (Toàn quốc)",
        "bac": "🏔️ Miền Bắc",
        "trung": "🏖️ Miền Trung", 
        "nam": "🌴 Miền Nam"
    }
    
    def __init__(self, root):
        self.root = root
        self.root.title("🌐 KiotProxy Manager v1.0")
        self.root.geometry("950x750")
        self.root.minsize(850, 650)
        
        # Variables
        self.api_key = tk.StringVar()
        self.selected_region = tk.StringVar(value="random")
        self.proxy_history = []
        self.current_proxy_data = None
        self.countdown_job = None
        
        # Setup UI
        self.setup_styles()
        self.create_ui()
        
        # Initial log
        self.log("🚀 KiotProxy Manager đã khởi động", "info")
        self.log("📝 Nhập API Key và chọn vùng để bắt đầu", "info")
        
    def setup_styles(self):
        """Configure ttk styles"""
        self.style = ttk.Style()
        self.style.configure('Title.TLabel', font=('Segoe UI', 12, 'bold'))
        self.style.configure('Header.TLabel', font=('Segoe UI', 10, 'bold'))
        self.style.configure('Success.TLabel', foreground='#28a745')
        self.style.configure('Error.TLabel', foreground='#dc3545')
        self.style.configure('Warning.TLabel', foreground='#ffc107')
        self.style.configure('Info.TLabel', foreground='#17a2b8')
        self.style.configure('Big.TButton', font=('Segoe UI', 10))
        
    def create_ui(self):
        """Create the main user interface"""
        # Main container with padding
        main_frame = ttk.Frame(self.root, padding="10")
        main_frame.pack(fill=tk.BOTH, expand=True)
        
        # ===== Header Section =====
        self.create_header_section(main_frame)
        
        # ===== API Key Section =====
        self.create_api_key_section(main_frame)
        
        # ===== Action Buttons Section =====
        self.create_action_section(main_frame)
        
        # ===== Proxy Info Section =====
        self.create_proxy_info_section(main_frame)
        
        # ===== Proxy History Section =====
        self.create_history_section(main_frame)
        
        # ===== Log Section =====
        self.create_log_section(main_frame)
        
        # ===== Status Bar =====
        self.create_status_bar(main_frame)
        
    def create_header_section(self, parent):
        """Create header with title"""
        header_frame = ttk.Frame(parent)
        header_frame.pack(fill=tk.X, pady=(0, 10))
        
        ttk.Label(header_frame, text="🌐 KiotProxy Manager", 
                 style='Title.TLabel').pack(side=tk.LEFT)
        
        # Help button
        ttk.Button(header_frame, text="❓ Hướng dẫn", 
                  command=self.show_help).pack(side=tk.RIGHT)
        
    def create_api_key_section(self, parent):
        """Create API key input section"""
        api_frame = ttk.LabelFrame(parent, text="🔑 Xác thực API", padding="10")
        api_frame.pack(fill=tk.X, pady=(0, 10))
        
        # Row 1: API Key input
        row1 = ttk.Frame(api_frame)
        row1.pack(fill=tk.X, pady=(0, 5))
        
        ttk.Label(row1, text="API Key:").pack(side=tk.LEFT, padx=(0, 10))
        
        self.api_entry = ttk.Entry(row1, textvariable=self.api_key, width=50, show="*")
        self.api_entry.pack(side=tk.LEFT, fill=tk.X, expand=True, padx=(0, 10))
        
        self.show_key_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(row1, text="👁️ Hiện", variable=self.show_key_var,
                       command=self.toggle_key_visibility).pack(side=tk.LEFT)
        
        # Row 2: Region selection
        row2 = ttk.Frame(api_frame)
        row2.pack(fill=tk.X)
        
        ttk.Label(row2, text="Vùng:").pack(side=tk.LEFT, padx=(0, 10))
        
        for value, text in self.REGIONS.items():
            ttk.Radiobutton(row2, text=text, variable=self.selected_region,
                           value=value).pack(side=tk.LEFT, padx=(0, 15))
                           
    def create_action_section(self, parent):
        """Create action buttons section"""
        action_frame = ttk.LabelFrame(parent, text="⚡ Thao tác", padding="10")
        action_frame.pack(fill=tk.X, pady=(0, 10))
        
        # Button container
        btn_frame = ttk.Frame(action_frame)
        btn_frame.pack(fill=tk.X)
        
        # Main action buttons
        self.get_new_btn = ttk.Button(btn_frame, text="📥 Lấy Proxy Mới", 
                                      command=self.get_new_proxy, width=18, style='Big.TButton')
        self.get_new_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        self.get_current_btn = ttk.Button(btn_frame, text="📍 Proxy Hiện tại",
                                          command=self.get_current_proxy, width=18, style='Big.TButton')
        self.get_current_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        self.release_btn = ttk.Button(btn_frame, text="🚪 Thoát Proxy",
                                      command=self.release_proxy, width=18, style='Big.TButton')
        self.release_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        self.test_btn = ttk.Button(btn_frame, text="🧪 Test Proxy",
                                   command=self.test_current_proxy, width=18, style='Big.TButton')
        self.test_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        self.copy_http_btn = ttk.Button(btn_frame, text="📋 Copy HTTP",
                                        command=lambda: self.copy_proxy("http"), width=12)
        self.copy_http_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        self.copy_socks5_btn = ttk.Button(btn_frame, text="📋 Copy SOCKS5",
                                          command=lambda: self.copy_proxy("socks5"), width=14)
        self.copy_socks5_btn.pack(side=tk.LEFT)
        
    def create_proxy_info_section(self, parent):
        """Create proxy information display section"""
        info_frame = ttk.LabelFrame(parent, text="🌐 Thông tin Proxy", padding="10")
        info_frame.pack(fill=tk.X, pady=(0, 10))
        
        # Create two columns
        left_frame = ttk.Frame(info_frame)
        left_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 10))
        
        right_frame = ttk.Frame(info_frame)
        right_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        
        # Left column - Connection info
        conn_frame = ttk.LabelFrame(left_frame, text="Kết nối", padding="5")
        conn_frame.pack(fill=tk.BOTH, expand=True)
        
        # HTTP Proxy
        http_row = ttk.Frame(conn_frame)
        http_row.pack(fill=tk.X, pady=2)
        ttk.Label(http_row, text="HTTP:", width=10, anchor='w').pack(side=tk.LEFT)
        self.http_var = tk.StringVar(value="-")
        self.http_entry = ttk.Entry(http_row, textvariable=self.http_var, state='readonly', width=30)
        self.http_entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        
        # SOCKS5 Proxy
        socks_row = ttk.Frame(conn_frame)
        socks_row.pack(fill=tk.X, pady=2)
        ttk.Label(socks_row, text="SOCKS5:", width=10, anchor='w').pack(side=tk.LEFT)
        self.socks5_var = tk.StringVar(value="-")
        self.socks5_entry = ttk.Entry(socks_row, textvariable=self.socks5_var, state='readonly', width=30)
        self.socks5_entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        
        # Real IP
        ip_row = ttk.Frame(conn_frame)
        ip_row.pack(fill=tk.X, pady=2)
        ttk.Label(ip_row, text="Real IP:", width=10, anchor='w').pack(side=tk.LEFT)
        self.real_ip_var = tk.StringVar(value="-")
        ttk.Label(ip_row, textvariable=self.real_ip_var, foreground='#007bff').pack(side=tk.LEFT)
        
        # Location
        loc_row = ttk.Frame(conn_frame)
        loc_row.pack(fill=tk.X, pady=2)
        ttk.Label(loc_row, text="Vị trí:", width=10, anchor='w').pack(side=tk.LEFT)
        self.location_var = tk.StringVar(value="-")
        ttk.Label(loc_row, textvariable=self.location_var, foreground='#28a745').pack(side=tk.LEFT)
        
        # Right column - Time info
        time_frame = ttk.LabelFrame(right_frame, text="Thời gian", padding="5")
        time_frame.pack(fill=tk.BOTH, expand=True)
        
        # TTL (Time to Live)
        ttl_row = ttk.Frame(time_frame)
        ttl_row.pack(fill=tk.X, pady=2)
        ttk.Label(ttl_row, text="TTL:", width=15, anchor='w').pack(side=tk.LEFT)
        self.ttl_var = tk.StringVar(value="-")
        ttk.Label(ttl_row, textvariable=self.ttl_var).pack(side=tk.LEFT)
        
        # TTC (Time to Change)
        ttc_row = ttk.Frame(time_frame)
        ttc_row.pack(fill=tk.X, pady=2)
        ttk.Label(ttc_row, text="Đổi IP sau:", width=15, anchor='w').pack(side=tk.LEFT)
        self.ttc_var = tk.StringVar(value="-")
        self.ttc_label = ttk.Label(ttc_row, textvariable=self.ttc_var, foreground='#dc3545', 
                                   font=('Segoe UI', 10, 'bold'))
        self.ttc_label.pack(side=tk.LEFT)
        
        # Expiration
        exp_row = ttk.Frame(time_frame)
        exp_row.pack(fill=tk.X, pady=2)
        ttk.Label(exp_row, text="Hết hạn:", width=15, anchor='w').pack(side=tk.LEFT)
        self.exp_var = tk.StringVar(value="-")
        ttk.Label(exp_row, textvariable=self.exp_var, foreground='#6c757d').pack(side=tk.LEFT)
        
        # Status
        status_row = ttk.Frame(time_frame)
        status_row.pack(fill=tk.X, pady=2)
        ttk.Label(status_row, text="Trạng thái:", width=15, anchor='w').pack(side=tk.LEFT)
        self.proxy_status_var = tk.StringVar(value="⚪ Chưa kết nối")
        self.proxy_status_label = ttk.Label(status_row, textvariable=self.proxy_status_var)
        self.proxy_status_label.pack(side=tk.LEFT)
        
    def create_history_section(self, parent):
        """Create proxy history section with treeview"""
        history_frame = ttk.LabelFrame(parent, text="📜 Lịch sử Proxy", padding="10")
        history_frame.pack(fill=tk.BOTH, expand=True, pady=(0, 10))
        
        # Treeview
        columns = ('STT', 'Thời gian', 'HTTP', 'SOCKS5', 'Vị trí', 'Trạng thái', 'Tốc độ')
        self.history_tree = ttk.Treeview(history_frame, columns=columns, show='headings', height=6)
        
        # Define headings and widths
        col_config = [
            ('STT', 40, 'center'),
            ('Thời gian', 80, 'center'),
            ('HTTP', 180, 'w'),
            ('SOCKS5', 180, 'w'),
            ('Vị trí', 100, 'center'),
            ('Trạng thái', 80, 'center'),
            ('Tốc độ', 70, 'center')
        ]
        
        for col, width, anchor in col_config:
            self.history_tree.heading(col, text=col)
            self.history_tree.column(col, width=width, anchor=anchor)
        
        # Scrollbar
        scrollbar = ttk.Scrollbar(history_frame, orient=tk.VERTICAL, command=self.history_tree.yview)
        self.history_tree.configure(yscrollcommand=scrollbar.set)
        
        self.history_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        
        # Bind events
        self.history_tree.bind('<Double-1>', self.on_history_double_click)
        self.history_tree.bind('<Button-3>', self.show_history_menu)
        
        # Context menu
        self.history_menu = tk.Menu(self.root, tearoff=0)
        self.history_menu.add_command(label="📋 Copy HTTP", command=lambda: self.copy_from_history("http"))
        self.history_menu.add_command(label="📋 Copy SOCKS5", command=lambda: self.copy_from_history("socks5"))
        self.history_menu.add_separator()
        self.history_menu.add_command(label="🧪 Test Proxy này", command=self.test_selected_history)
        self.history_menu.add_separator()
        self.history_menu.add_command(label="🗑️ Xóa lịch sử", command=self.clear_history)
        
    def create_log_section(self, parent):
        """Create log display section"""
        log_frame = ttk.LabelFrame(parent, text="📋 Log hoạt động", padding="10")
        log_frame.pack(fill=tk.BOTH, expand=True, pady=(0, 10))
        
        # Log text with scrollbar
        self.log_text = scrolledtext.ScrolledText(log_frame, height=6, font=('Consolas', 9),
                                                   wrap=tk.WORD)
        self.log_text.pack(fill=tk.BOTH, expand=True)
        
        # Configure tags for colored text
        self.log_text.tag_configure('info', foreground='#17a2b8')
        self.log_text.tag_configure('success', foreground='#28a745')
        self.log_text.tag_configure('error', foreground='#dc3545')
        self.log_text.tag_configure('warning', foreground='#ffc107')
        self.log_text.tag_configure('time', foreground='#6c757d')
        
        # Clear button
        btn_frame = ttk.Frame(log_frame)
        btn_frame.pack(fill=tk.X, pady=(5, 0))
        ttk.Button(btn_frame, text="🗑️ Xóa Log", command=self.clear_log).pack(side=tk.RIGHT)
        
    def create_status_bar(self, parent):
        """Create status bar at bottom"""
        status_frame = ttk.Frame(parent)
        status_frame.pack(fill=tk.X)
        
        self.statusbar = ttk.Label(status_frame, text="✅ Sẵn sàng", relief=tk.SUNKEN, anchor=tk.W)
        self.statusbar.pack(side=tk.LEFT, fill=tk.X, expand=True)
        
        # Progress bar (hidden by default)
        self.progress = ttk.Progressbar(status_frame, mode='indeterminate', length=150)
        
    # ==================== Helper Methods ====================
    
    def toggle_key_visibility(self):
        """Toggle API key visibility"""
        self.api_entry.configure(show="" if self.show_key_var.get() else "*")
        
    def log(self, message, level='info'):
        """Add message to log with timestamp"""
        timestamp = datetime.now().strftime("%H:%M:%S")
        self.log_text.insert(tk.END, f"[{timestamp}] ", 'time')
        self.log_text.insert(tk.END, f"{message}\n", level)
        self.log_text.see(tk.END)
        
    def clear_log(self):
        """Clear log text"""
        self.log_text.delete(1.0, tk.END)
        self.log("📋 Log đã được xóa", "info")
        
    def show_loading(self, show=True, message="Đang xử lý..."):
        """Show or hide loading indicator"""
        if show:
            self.progress.pack(side=tk.RIGHT, padx=(10, 0))
            self.progress.start(10)
            self.statusbar.config(text=f"⏳ {message}")
            self.disable_buttons(True)
        else:
            self.progress.stop()
            self.progress.pack_forget()
            self.statusbar.config(text="✅ Sẵn sàng")
            self.disable_buttons(False)
            
    def disable_buttons(self, disabled):
        """Enable/disable action buttons"""
        state = 'disabled' if disabled else 'normal'
        self.get_new_btn.config(state=state)
        self.get_current_btn.config(state=state)
        self.release_btn.config(state=state)
        self.test_btn.config(state=state)
        
    def validate_key(self):
        """Validate if API key is entered"""
        if not self.api_key.get().strip():
            messagebox.showwarning("⚠️ Cảnh báo", "Vui lòng nhập API Key!")
            return False
        return True
        
    # ==================== API Methods ====================
    
    def make_request(self, endpoint, params=None):
        """Make API request to KiotProxy"""
        url = f"{self.BASE_URL}/{endpoint}"
        if params is None:
            params = {}
        params['key'] = self.api_key.get().strip()
        
        try:
            response = requests.get(url, params=params, timeout=30)
            return response.json()
        except requests.exceptions.Timeout:
            raise Exception("⏱️ Request timeout - Server không phản hồi")
        except requests.exceptions.ConnectionError:
            raise Exception("🔌 Không thể kết nối đến server")
        except json.JSONDecodeError:
            raise Exception("📄 Server trả về dữ liệu không hợp lệ")
        except Exception as e:
            raise Exception(f"❌ Lỗi: {str(e)}")
            
    def get_new_proxy(self):
        """Get new proxy or rotate IP"""
        if not self.validate_key():
            return
            
        def request_thread():
            self.root.after(0, lambda: self.show_loading(True, "Đang lấy proxy mới..."))
            self.log(f"📥 Đang lấy proxy mới (Vùng: {self.REGIONS[self.selected_region.get()]})", "info")
            
            try:
                result = self.make_request("new", {"region": self.selected_region.get()})
                
                if result.get('success'):
                    data = result.get('data', {})
                    self.root.after(0, lambda: self.update_proxy_display(data))
                    self.root.after(0, lambda: self.add_to_history(data))
                    self.log(f"✅ Lấy proxy thành công: {data.get('http', 'N/A')}", "success")
                    self.log(f"📍 Vị trí: {data.get('location', 'N/A')}", "info")
                else:
                    error = result.get('message', result.get('error', 'Unknown error'))
                    self.log(f"❌ Lỗi: {error}", "error")
                    
                    # Handle specific errors
                    if result.get('error') == 'KEY_NOT_FOUND':
                        self.log("🔑 API Key không tồn tại hoặc không hợp lệ", "warning")
                    elif 'ttc' in str(error).lower() or 'wait' in str(error).lower():
                        self.log("⏳ Chưa đến thời gian đổi IP, vui lòng đợi", "warning")
                        
            except Exception as e:
                self.log(str(e), "error")
            finally:
                self.root.after(0, lambda: self.show_loading(False))
                
        threading.Thread(target=request_thread, daemon=True).start()
        
    def get_current_proxy(self):
        """Get current proxy information"""
        if not self.validate_key():
            return
            
        def request_thread():
            self.root.after(0, lambda: self.show_loading(True, "Đang lấy thông tin proxy..."))
            self.log("📍 Đang lấy thông tin proxy hiện tại...", "info")
            
            try:
                result = self.make_request("current")
                
                if result.get('success'):
                    data = result.get('data', {})
                    self.root.after(0, lambda: self.update_proxy_display(data))
                    self.log(f"✅ Proxy hiện tại: {data.get('http', 'N/A')}", "success")
                else:
                    error = result.get('message', result.get('error', 'Unknown error'))
                    self.log(f"❌ {error}", "error")
                    
                    if result.get('error') == 'PROXY_NOT_FOUND_BY_KEY':
                        self.log("💡 Chưa có proxy nào được gán cho key này", "warning")
                        self.root.after(0, self.clear_proxy_display)
                        
            except Exception as e:
                self.log(str(e), "error")
            finally:
                self.root.after(0, lambda: self.show_loading(False))
                
        threading.Thread(target=request_thread, daemon=True).start()
        
    def release_proxy(self):
        """Release current proxy from key"""
        if not self.validate_key():
            return
            
        if not messagebox.askyesno("🚪 Xác nhận", "Bạn có chắc muốn thoát proxy hiện tại?"):
            return
            
        def request_thread():
            self.root.after(0, lambda: self.show_loading(True, "Đang thoát proxy..."))
            self.log("🚪 Đang thoát proxy...", "info")
            
            try:
                result = self.make_request("out")
                
                if result.get('success'):
                    self.log("✅ Đã thoát proxy thành công", "success")
                    self.root.after(0, self.clear_proxy_display)
                else:
                    error = result.get('message', result.get('error', 'Unknown error'))
                    self.log(f"❌ {error}", "error")
                    
            except Exception as e:
                self.log(str(e), "error")
            finally:
                self.root.after(0, lambda: self.show_loading(False))
                
        threading.Thread(target=request_thread, daemon=True).start()
        
    def test_current_proxy(self):
        """Test the current proxy"""
        if not self.current_proxy_data:
            messagebox.showinfo("ℹ️ Thông báo", "Chưa có proxy để test. Hãy lấy proxy trước!")
            return
            
        def test_thread():
            self.root.after(0, lambda: self.show_loading(True, "Đang test proxy..."))
            
            http_proxy = self.current_proxy_data.get('http', '')
            socks5_proxy = self.current_proxy_data.get('socks5', '')
            
            self.log(f"🧪 Đang test proxy: {http_proxy}", "info")
            
            # Test HTTP proxy
            http_result = self.test_proxy_connection(f"http://{http_proxy}")
            
            # Test SOCKS5 proxy
            socks5_result = self.test_proxy_connection(f"socks5://{socks5_proxy}")
            
            # Update status
            if http_result[0] or socks5_result[0]:
                speed = http_result[0] or socks5_result[0]
                self.log(f"✅ Proxy hoạt động tốt! Tốc độ: {speed}ms", "success")
                self.root.after(0, lambda: self.proxy_status_var.set(f"🟢 Hoạt động ({speed}ms)"))
                
                # Update history
                self.root.after(0, lambda: self.update_history_status(
                    http_proxy, "🟢 OK", f"{speed}ms"))
            else:
                self.log(f"❌ Proxy không hoạt động: {http_result[1]}", "error")
                self.root.after(0, lambda: self.proxy_status_var.set("🔴 Không hoạt động"))
                self.root.after(0, lambda: self.update_history_status(
                    http_proxy, "🔴 Lỗi", "-"))
                    
            self.root.after(0, lambda: self.show_loading(False))
            
        threading.Thread(target=test_thread, daemon=True).start()
        
    def test_proxy_connection(self, proxy_url, timeout=10):
        """Test a proxy connection and return (speed_ms, error)"""
        proxies = {'http': proxy_url, 'https': proxy_url}
        test_urls = [
            'https://api.ipify.org?format=json',
            'http://httpbin.org/ip',
            'https://ifconfig.me/ip'
        ]
        
        for url in test_urls:
            try:
                start = time.time()
                response = requests.get(url, proxies=proxies, timeout=timeout)
                elapsed = int((time.time() - start) * 1000)
                
                if response.status_code == 200:
                    return (elapsed, None)
            except requests.exceptions.ProxyError:
                return (None, "Proxy Error")
            except requests.exceptions.Timeout:
                return (None, "Timeout")
            except Exception as e:
                continue
                
        return (None, "Connection Failed")
        
    # ==================== UI Update Methods ====================
    
    def update_proxy_display(self, data):
        """Update proxy information display"""
        self.current_proxy_data = data
        
        # Update connection info
        self.http_var.set(data.get('http', '-'))
        self.socks5_var.set(data.get('socks5', '-'))
        self.real_ip_var.set(data.get('realIpAddress', '-'))
        self.location_var.set(data.get('location', '-'))
        
        # Update time info
        ttl = data.get('ttl', 0)
        ttc = data.get('ttc', 0)
        
        self.ttl_var.set(f"{ttl} giây ({ttl//60} phút)")
        
        # Expiration time
        exp_timestamp = data.get('expirationAt', 0)
        if exp_timestamp:
            exp_time = datetime.fromtimestamp(exp_timestamp / 1000)
            self.exp_var.set(exp_time.strftime("%H:%M:%S %d/%m/%Y"))
        else:
            self.exp_var.set("-")
            
        # Status
        self.proxy_status_var.set("🟡 Chưa test")
        
        # Start countdown for TTC
        self.start_ttc_countdown(ttc)
        
    def clear_proxy_display(self):
        """Clear proxy information display"""
        self.current_proxy_data = None
        
        self.http_var.set("-")
        self.socks5_var.set("-")
        self.real_ip_var.set("-")
        self.location_var.set("-")
        self.ttl_var.set("-")
        self.ttc_var.set("-")
        self.exp_var.set("-")
        self.proxy_status_var.set("⚪ Chưa kết nối")
        
        # Stop countdown
        if self.countdown_job:
            self.root.after_cancel(self.countdown_job)
            self.countdown_job = None
            
    def start_ttc_countdown(self, seconds):
        """Start countdown timer for TTC"""
        # Cancel existing countdown
        if self.countdown_job:
            self.root.after_cancel(self.countdown_job)
            
        def update_countdown(remaining):
            if remaining <= 0:
                self.ttc_var.set("✅ Có thể đổi IP!")
                self.ttc_label.config(foreground='#28a745')
                return
                
            mins, secs = divmod(remaining, 60)
            self.ttc_var.set(f"{mins:02d}:{secs:02d}")
            
            # Change color based on time
            if remaining <= 10:
                self.ttc_label.config(foreground='#28a745')
            elif remaining <= 30:
                self.ttc_label.config(foreground='#ffc107')
            else:
                self.ttc_label.config(foreground='#dc3545')
                
            self.countdown_job = self.root.after(1000, lambda: update_countdown(remaining - 1))
            
        update_countdown(seconds)
        
    def add_to_history(self, data):
        """Add proxy to history"""
        timestamp = datetime.now().strftime("%H:%M:%S")
        
        entry = {
            'time': timestamp,
            'http': data.get('http', '-'),
            'socks5': data.get('socks5', '-'),
            'location': data.get('location', '-'),
            'status': '🟡 Chưa test',
            'speed': '-',
            'data': data
        }
        
        self.proxy_history.insert(0, entry)
        
        # Keep only last 50 entries
        if len(self.proxy_history) > 50:
            self.proxy_history = self.proxy_history[:50]
            
        self.update_history_tree()
        
    def update_history_tree(self):
        """Update history treeview"""
        # Clear current items
        for item in self.history_tree.get_children():
            self.history_tree.delete(item)
            
        # Add entries
        for i, entry in enumerate(self.proxy_history, 1):
            self.history_tree.insert('', tk.END, values=(
                i,
                entry['time'],
                entry['http'],
                entry['socks5'],
                entry['location'],
                entry['status'],
                entry['speed']
            ))
            
    def update_history_status(self, http_proxy, status, speed):
        """Update status of a proxy in history"""
        for entry in self.proxy_history:
            if entry['http'] == http_proxy:
                entry['status'] = status
                entry['speed'] = speed
                break
        self.update_history_tree()
        
    def clear_history(self):
        """Clear proxy history"""
        if messagebox.askyesno("🗑️ Xác nhận", "Xóa toàn bộ lịch sử proxy?"):
            self.proxy_history.clear()
            self.update_history_tree()
            self.log("🗑️ Đã xóa lịch sử proxy", "info")
            
    # ==================== Copy & Export Methods ====================
    
    def copy_proxy(self, proxy_type):
        """Copy proxy to clipboard"""
        if not self.current_proxy_data:
            messagebox.showinfo("ℹ️ Thông báo", "Chưa có proxy để copy!")
            return
            
        proxy = self.current_proxy_data.get(proxy_type, '')
        if proxy:
            self.root.clipboard_clear()
            self.root.clipboard_append(proxy)
            self.log(f"📋 Đã copy {proxy_type.upper()}: {proxy}", "success")
        else:
            messagebox.showinfo("ℹ️ Thông báo", f"Không có proxy {proxy_type.upper()}!")
            
    def copy_from_history(self, proxy_type):
        """Copy proxy from selected history item"""
        selection = self.history_tree.selection()
        if not selection:
            return
            
        item = self.history_tree.item(selection[0])
        values = item['values']
        
        proxy = values[2] if proxy_type == "http" else values[3]  # HTTP at index 2, SOCKS5 at index 3
        
        self.root.clipboard_clear()
        self.root.clipboard_append(proxy)
        self.log(f"📋 Đã copy {proxy_type.upper()}: {proxy}", "success")
        
    def on_history_double_click(self, event):
        """Handle double click on history item"""
        selection = self.history_tree.selection()
        if not selection:
            return
            
        item = self.history_tree.item(selection[0])
        values = item['values']
        
        # Copy HTTP proxy on double click
        self.root.clipboard_clear()
        self.root.clipboard_append(values[2])
        self.log(f"📋 Đã copy HTTP: {values[2]}", "success")
        
    def show_history_menu(self, event):
        """Show context menu for history"""
        try:
            self.history_tree.selection_set(self.history_tree.identify_row(event.y))
            self.history_menu.tk_popup(event.x_root, event.y_root)
        finally:
            self.history_menu.grab_release()
            
    def test_selected_history(self):
        """Test selected proxy from history"""
        selection = self.history_tree.selection()
        if not selection:
            return
            
        item = self.history_tree.item(selection[0])
        values = item['values']
        http_proxy = values[2]
        
        def test_thread():
            self.root.after(0, lambda: self.show_loading(True, "Đang test proxy..."))
            self.log(f"🧪 Đang test proxy: {http_proxy}", "info")
            
            result = self.test_proxy_connection(f"http://{http_proxy}")
            
            if result[0]:
                self.log(f"✅ Proxy hoạt động! Tốc độ: {result[0]}ms", "success")
                self.root.after(0, lambda: self.update_history_status(
                    http_proxy, "🟢 OK", f"{result[0]}ms"))
            else:
                self.log(f"❌ Proxy lỗi: {result[1]}", "error")
                self.root.after(0, lambda: self.update_history_status(
                    http_proxy, "🔴 Lỗi", "-"))
                    
            self.root.after(0, lambda: self.show_loading(False))
            
        threading.Thread(target=test_thread, daemon=True).start()
        
    # ==================== Help & Info ====================
    
    def show_help(self):
        """Show help dialog"""
        help_text = """
🌐 KiotProxy Manager - Hướng dẫn sử dụng

📌 BƯỚC 1: Nhập API Key
   - Nhập API Key của bạn vào ô "API Key"
   - Chọn vùng proxy mong muốn (Bắc/Trung/Nam/Ngẫu nhiên)

📌 BƯỚC 2: Lấy Proxy
   - Nhấn "📥 Lấy Proxy Mới" để lấy proxy mới
   - Nhấn "📍 Proxy Hiện tại" để xem proxy đang dùng

📌 BƯỚC 3: Sử dụng Proxy
   - Copy HTTP hoặc SOCKS5 proxy để sử dụng
   - Format: ip:port

📌 BƯỚC 4: Đổi IP
   - Đợi hết thời gian TTC (Time to Change)
   - Nhấn "📥 Lấy Proxy Mới" để đổi IP

📌 CÁC NÚT CHỨC NĂNG:
   • 📥 Lấy Proxy Mới: Lấy proxy mới hoặc đổi IP
   • 📍 Proxy Hiện tại: Xem thông tin proxy đang dùng
   • 🚪 Thoát Proxy: Ngắt kết nối proxy khỏi key
   • 🧪 Test Proxy: Kiểm tra proxy có hoạt động không
   • 📋 Copy: Copy proxy vào clipboard

📌 THÔNG TIN HIỂN THỊ:
   • HTTP/SOCKS5: Địa chỉ proxy (ip:port)
   • TTL: Thời gian sống của proxy
   • TTC: Thời gian chờ để đổi IP tiếp theo
   • Hết hạn: Thời điểm proxy hết hạn

💡 MẸO:
   - Double-click vào lịch sử để copy nhanh
   - Chuột phải vào lịch sử để xem thêm tùy chọn
"""
        
        help_window = tk.Toplevel(self.root)
        help_window.title("❓ Hướng dẫn sử dụng")
        help_window.geometry("500x550")
        help_window.resizable(False, False)
        
        text = scrolledtext.ScrolledText(help_window, font=('Segoe UI', 10), wrap=tk.WORD)
        text.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)
        text.insert(tk.END, help_text)
        text.config(state='disabled')
        
        ttk.Button(help_window, text="Đóng", 
                  command=help_window.destroy).pack(pady=(0, 10))


def main():
    """Main entry point"""
    root = tk.Tk()
    
    # Set icon if available
    try:
        root.iconbitmap('icon.ico')
    except:
        pass
    
    app = KiotProxyApp(root)
    
    # Center window
    root.update_idletasks()
    width = root.winfo_width()
    height = root.winfo_height()
    x = (root.winfo_screenwidth() // 2) - (width // 2)
    y = (root.winfo_screenheight() // 2) - (height // 2)
    root.geometry(f'+{x}+{y}')
    
    root.mainloop()


if __name__ == "__main__":
    main()