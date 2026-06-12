#!/usr/bin/env python3
"""
PoE2 物价追踪器 - 本地物价数据库 + Web UI
自动从 poe.ninja 同步价格，支持中英文查询
"""

import json
import os
import time
import threading
import urllib.request
from datetime import datetime
from flask import Flask, render_template, jsonify, request

# 配置
SYNC_INTERVAL = 1800  # 30分钟 = 1800秒
DATA_DIR = os.path.dirname(os.path.abspath(__file__))
PRICES_FILE = os.path.join(DATA_DIR, 'prices.json')
MAPPING_FILE = os.path.join(DATA_DIR, 'item_names_cn.json')

# poe.ninja API 配置
LEAGUE = 'Standard'
CATEGORIES = ['Currency', 'Runes']

app = Flask(__name__)

# 全局变量
prices_db = {
    'last_sync': None,
    'sync_count': 0,
    'items': {},
    'rates': {}
}

def load_mapping():
    """加载中英文映射表"""
    if os.path.exists(MAPPING_FILE):
        with open(MAPPING_FILE, 'r', encoding='utf-8') as f:
            return json.load(f)
    return {}

def save_prices():
    """保存价格数据到本地文件"""
    with open(PRICES_FILE, 'w', encoding='utf-8') as f:
        json.dump(prices_db, f, ensure_ascii=False, indent=2)

def load_prices():
    """从本地文件加载价格数据"""
    global prices_db
    if os.path.exists(PRICES_FILE):
        with open(PRICES_FILE, 'r', encoding='utf-8') as f:
            prices_db = json.load(f)
            print(f"[加载] 本地数据: {len(prices_db.get('items', {}))} 个物品")

def fetch_from_ninja(category):
    """从 poe.ninja 获取价格数据"""
    url = f'https://poe.ninja/poe2/api/economy/exchange/current/overview?league={LEAGUE}&type={category}'
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read())
    except Exception as e:
        print(f"[错误] 获取 {category} 失败: {e}")
        return None

def sync_prices():
    """同步价格数据"""
    global prices_db
    
    print("[同步] 开始同步价格数据...")
    start_time = time.time()
    
    mapping = load_mapping()
    # 反向映射: 中文 -> 英文
    cn_to_en = {}
    for en, cn in mapping.items():
        cn_to_en[cn.lower()] = en
    
    new_items = {}
    new_rates = {}
    total_items = 0
    
    for category in CATEGORIES:
        data = fetch_from_ninja(category)
        if not data:
            continue
        
        # 获取汇率
        core = data.get('core', {})
        rates = core.get('rates', {})
        if rates:
            new_rates = rates
            new_rates['primary'] = core.get('primary', 'divine')
        
        # 获取物品数据
        items = data.get('items', [])
        lines = data.get('lines', [])
        
        # 构建 id -> name 映射
        id_to_name = {}
        for item in items:
            id_to_name[item.get('id')] = item.get('name')
        
        # 获取价格
        for line in lines:
            item_id = line.get('id')
            primary_value = line.get('primaryValue', 0)
            
            if item_id and item_id in id_to_name:
                en_name = id_to_name[item_id]
                en_key = en_name.lower()
                
                # 计算各种货币价格
                divine_value = primary_value
                exalted_value = 0
                chaos_value = 0
                
                if new_rates.get('primary') == 'divine':
                    exalted_value = primary_value * new_rates.get('exalted', 1)
                    chaos_value = primary_value * new_rates.get('chaos', 1)
                elif new_rates.get('primary') == 'exalted':
                    divine_value = primary_value / new_rates.get('exalted', 1)
                    exalted_value = primary_value
                    chaos_value = primary_value * new_rates.get('chaos', 1) / new_rates.get('exalted', 1)
                
                # 查找中文名
                cn_name = mapping.get(en_key, '')
                
                new_items[en_key] = {
                    'en_name': en_name,
                    'cn_name': cn_name,
                    'divine': round(divine_value, 6),
                    'exalted': round(exalted_value, 2),
                    'chaos': round(chaos_value, 2),
                    'category': category,
                    'item_id': item_id
                }
                total_items += 1
    
    # 更新数据库
    prices_db['items'] = new_items
    prices_db['rates'] = new_rates
    prices_db['last_sync'] = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
    prices_db['sync_count'] = prices_db.get('sync_count', 0) + 1
    prices_db['total_items'] = total_items
    
    save_prices()
    
    elapsed = time.time() - start_time
    print(f"[同步] 完成! {total_items} 个物品, 耗时 {elapsed:.1f}秒")
    
    return total_items

def sync_loop():
    """同步循环"""
    while True:
        try:
            sync_prices()
        except Exception as e:
            print(f"[错误] 同步失败: {e}")
        time.sleep(SYNC_INTERVAL)

def lookup_price(query):
    """查询价格 - 优先本地，然后 ninja"""
    query_lower = query.lower().strip()
    mapping = load_mapping()
    
    # 1. 先在本地数据库查找
    # 英文名直接查找
    if query_lower in prices_db.get('items', {}):
        return prices_db['items'][query_lower]
    
    # 中文名查找 - 通过映射表
    cn_to_en = {}
    for en, cn in mapping.items():
        cn_to_en[cn.lower()] = en
    
    if query_lower in cn_to_en:
        en_key = cn_to_en[query_lower]
        if en_key in prices_db.get('items', {}):
            return prices_db['items'][en_key]
    
    # 模糊匹配
    for key, item in prices_db.get('items', {}).items():
        if query_lower in key or query_lower in item.get('cn_name', '').lower():
            return item
    
    # 2. 本地没有，尝试从 ninja 获取
    print(f"[查询] 本地未找到 '{query}'，尝试从 ninja 获取...")
    
    # 重新同步后再次查找
    sync_prices()
    
    if query_lower in prices_db.get('items', {}):
        return prices_db['items'][query_lower]
    
    if query_lower in cn_to_en:
        en_key = cn_to_en[query_lower]
        if en_key in prices_db.get('items', {}):
            return prices_db['items'][en_key]
    
    return None

# ============ Web Routes ============

@app.route('/')
def index():
    """主页"""
    return render_template('index.html')

@app.route('/api/status')
def api_status():
    """获取同步状态"""
    return jsonify({
        'last_sync': prices_db.get('last_sync'),
        'sync_count': prices_db.get('sync_count', 0),
        'total_items': prices_db.get('total_items', 0),
        'rates': prices_db.get('rates', {})
    })

@app.route('/api/sync', methods=['POST'])
def api_sync():
    """手动触发同步"""
    try:
        count = sync_prices()
        return jsonify({'success': True, 'count': count})
    except Exception as e:
        return jsonify({'success': False, 'error': str(e)})

@app.route('/api/search')
def api_search():
    """搜索物品"""
    query = request.args.get('q', '').strip()
    if not query:
        return jsonify({'results': []})
    
    results = []
    query_lower = query.lower()
    mapping = load_mapping()
    
    # 中文 -> 英文 映射
    cn_to_en = {}
    for en, cn in mapping.items():
        cn_to_en[cn.lower()] = en
    
    for key, item in prices_db.get('items', {}).items():
        en_name = item.get('en_name', '').lower()
        cn_name = item.get('cn_name', '').lower()
        
        if query_lower in key or query_lower in en_name or query_lower in cn_name:
            results.append(item)
    
    return jsonify({'results': results[:20]})  # 最多返回20个结果

@app.route('/api/lookup')
def api_lookup():
    """查询单个物品"""
    query = request.args.get('q', '').strip()
    if not query:
        return jsonify({'found': False})
    
    result = lookup_price(query)
    if result:
        return jsonify({'found': True, 'item': result})
    else:
        return jsonify({'found': False})

@app.route('/api/items')
def api_items():
    """获取所有物品"""
    category = request.args.get('category', '')
    items = list(prices_db.get('items', {}).values())
    
    if category:
        items = [i for i in items if i.get('category', '').lower() == category.lower()]
    
    return jsonify({'items': items, 'total': len(items)})

# ============ Main ============

if __name__ == '__main__':
    print("=" * 50)
    print("  PoE2 物价追踪器")
    print("=" * 50)
    
    # 加载本地数据
    load_prices()
    
    # 首次同步
    if not prices_db.get('last_sync'):
        print("[启动] 首次同步...")
        sync_prices()
    
    # 启动同步线程
    sync_thread = threading.Thread(target=sync_loop, daemon=True)
    sync_thread.start()
    print(f"[启动] 自动同步: 每 {SYNC_INTERVAL // 60} 分钟")
    
    # 启动 Web 服务
    print("[启动] Web UI: http://localhost:5000")
    print("=" * 50)
    
    app.run(host='0.0.0.0', port=5000, debug=False)
