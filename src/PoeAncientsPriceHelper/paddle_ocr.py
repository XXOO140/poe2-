#!/usr/bin/env python3
"""
PaddleOCR 识别脚本 - 用于 PoE2 物价助手
使用 PaddleOCR 进行中文文字识别
"""

import sys
import json
import os
import base64
from pathlib import Path

# 设置环境变量，减少 PaddleOCR 的日志输出
os.environ['GLOG_minloglevel'] = '2'
os.environ['FLAGS_minloglevel'] = '2'

def setup_paddleocr():
    """初始化 PaddleOCR"""
    try:
        from paddleocr import PaddleOCR
        # 使用中文模型，支持简体和繁体
        # 使用新参数名
        ocr = PaddleOCR(
            use_textline_orientation=True,  # 使用方向分类器
            lang='ch',  # 中文模型
            show_log=False,  # 不显示日志
            use_gpu=False,  # 使用 CPU
            text_detection_model_dir=None,  # 使用默认模型
            text_recognition_model_dir=None,
            textline_orientation_model_dir=None
        )
        return ocr
    except Exception as e:
        print(json.dumps({
            'success': False,
            'error': f'初始化 PaddleOCR 失败: {str(e)}',
            'items': []
        }))
        sys.exit(1)

def recognize_image(ocr, image_path):
    """识别图片中的文字"""
    try:
        result = ocr.ocr(image_path, cls=True)
        
        items = []
        if result and len(result) > 0:
            for line in result[0]:
                if line and len(line) >= 2:
                    bbox = line[0]  # 边界框坐标
                    text_info = line[1]  # (文字, 置信度)
                    
                    if text_info and len(text_info) >= 2:
                        text = text_info[0]
                        confidence = text_info[1]
                        
                        # 计算中心 Y 坐标
                        if bbox and len(bbox) >= 4:
                            y_coords = [point[1] for point in bbox]
                            center_y = int(sum(y_coords) / len(y_coords))
                        else:
                            center_y = 0
                        
                        items.append({
                            'text': text,
                            'confidence': float(confidence),
                            'center_y': center_y
                        })
        
        return {
            'success': True,
            'items': items,
            'count': len(items)
        }
    except Exception as e:
        return {
            'success': False,
            'error': str(e),
            'items': []
        }

def main():
    """主函数"""
    if len(sys.argv) < 2:
        print(json.dumps({
            'success': False,
            'error': '用法: python paddle_ocr.py <image_path>',
            'items': []
        }))
        sys.exit(1)
    
    input_data = sys.argv[1]
    
    # 初始化 PaddleOCR
    ocr = setup_paddleocr()
    
    # 判断输入是文件路径
    if os.path.exists(input_data):
        result = recognize_image(ocr, input_data)
    else:
        result = {
            'success': False,
            'error': f'文件不存在: {input_data}',
            'items': []
        }
    
    # 输出结果
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()
