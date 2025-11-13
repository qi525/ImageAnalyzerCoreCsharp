# -*- coding: utf-8 -*-
import os
import re
from PIL import Image, ImageFile
from datetime import datetime
import concurrent.futures 
import warnings 
from openpyxl.cell.cell import ILLEGAL_CHARACTERS_RE # 导入用于清理非法字符的正则
from typing import List, Dict, Any 
from tqdm import tqdm # [2025-10-31] 新增导入: 用于显示进度条和计数器

# 允许 Pillow 加载截断的图像文件，避免程序崩溃。
ImageFile.LOAD_TRUNCATED_IMAGES = True

# 全局变量，用于在警告处理函数中访问当前处理的文件路径
_current_processing_file = None

# 定义最大并发进程数 (通常是CPU核心数)
MAX_WORKERS = os.cpu_count() or 4

# --- 正向提示词的停用词列表 (用于提取核心词) ---
POSITIVE_PROMPT_STOP_WORDS = [
    # ----------------------------------------------------
    # 核心词汇，一行算一个部分
    # (已根据用户要求，将每行视为一个整体词组)
    # ----------------------------------------------------
    # 第一行
    # r"",
    # r"",
    # r"",
    # r"",
    # r"",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, atdan, ",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, ",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_\(machi0910\), mochirong, ",
    r"newest, 2025, (artist\:toosaka_asagi:1.2), novel_illustration, (artist\:torino_aqua:1.5), artist\:izumi_tsubasu,(artist\:oyuwari:1.4), (artist\:pottsness:1.2), artist\:yunsang, artist\:hito_komoru, artist\:akeyama_kitsune, artist\:fi-san, (artist\:rourou_\(been\):1.2), artist\:gweda, artist\:fuzichoco, artist\:shanguier, artist\:anmi, (artist\:missile228:1.2), (artist\:atdan:1.7), artist\:iizuki_tasuku, artist\:piromizu, artist\:binggong_asylum, artist\:sheya, artist\:dishwasher1910, (artist\:omone_hokoma_agm:1.2), artist\:puuzaki_puuna, artist\:m-da_s-tarou, artist\:cutesexyrobutts, artist\:houkisei, artist\:sora_72-iro, artist\:machi_\(machi0910\), artist\:mochirong, see-through, see-through_clothes, transparent, front_light, frontlight, flat_lighting, soft_light, ",
    r"newest, 2025, (toosaka_asagi:1.2), novel_illustration, (torino_aqua:1.2), izumi_tsubasu,(oyuwari:1.2), (pottsness:1.2), yunsang, hito_komoru, akeyama_kitsune, fi-san, (rourou_\(been\):1.2), gweda, fuzichoco, shanguier, anmi, (missile228:1.2), (atdan:1.2), iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, (omone_hokoma_agm:1.2), puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_\(machi0910\), mochirong, see-through, transparent, ",
    r"newest, 2025, (toosaka_asagi:1.2), novel_illustration, (torino_aqua:1.2), izumi_tsubasu,(oyuwari:1.4), (pottsness:1.2), yunsang, hito_komoru, akeyama_kitsune, fi-san, (rourou_\(been\):1.2), gweda, fuzichoco, shanguier, anmi, (missile228:1.2), (atdan:1.2), iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, (omone_hokoma_agm:1.2), puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_\(machi0910\), mochirong, see-through, transparent, ",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, oyuwari, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_\(machi0910\), mochirong, ",
    r"newest, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, ",
    r"newest,2026,toosaka_asagi,novel_illustration,torino_aqua,izumi_tsubasu,oyuwari,pottsness,yunsang,hito_komoru,akeyama_kitsune,fi-san,rourou_\(been\),gweda,fuzichoco,shanguier,anmi, ",
    r"newest, 2025, (artist\:toosaka_asagi:1.2), novel_illustration, (artist\:torino_aqua:1.5), artist\:izumi_tsubasu,(artist\:oyuwari:1.4), (artist\:pottsness:1.2), artist\:yunsang, artist\:hito_komoru, artist\:akeyama_kitsune, artist\:fi-san, (artist\:rourou_\(been\):1.2), artist\:gweda, artist\:fuzichoco, artist\:shanguier, artist\:anmi, (artist\:missile228:1.2), (artist\:atdan:1.7), artist\:iizuki_tasuku, artist\:piromizu, artist\:binggong_asylum, artist\:sheya, artist\:dishwasher1910, (artist\:omone_hokoma_agm:1.2), artist\:puuzaki_puuna, artist\:m-da_s-tarou, artist\:cutesexyrobutts, artist\:houkisei, artist\:sora_72-iro, artist\:machi_\(machi0910\), artist\:mochirong, see-through, see-through_clothes, transparent, front_light, frontlight, flat_lighting, soft_light, ",
    r"newest, 2025, (toosaka_asagi:1.2), novel_illustration, (torino_aqua:1.2), izumi_tsubasu,(oyuwari:1.2), (pottsness:1.2), yunsang, hito_komoru, akeyama_kitsune, fi-san, (rourou_\(been\):1.2), gweda, fuzichoco, shanguier, anmi, (missile228:1.2), (atdan:1.2), iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, (omone_hokoma_agm:1.2), puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_\(machi0910\), mochirong, see-through, transparent, ",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_\(machi0910\), mochirong, ",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, ",
    r"newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_\(been\), gweda, fuzichoco, shanguier, anmi, missile228, ",
    r"2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, ",
    r"newest,2025,toosaka_asagi,novel_illustration,torino_aqua,izumi_tsubasu,oyuwari,pottsness,yunsang,hito_komoru,akeyama_kitsune,fi-san,rourou_\(been\),gweda,fuzichoco,shanguier,anmi,missile228,",
    r"newest,2025,toosaka_asagi,novel_illustration,torino_aqua,izumi_tsubasu,oyuwari,pottsness,yunsang,hito_komoru,akeyama_kitsune,fi-san,rourou_\(been\),gweda,fuzichoco,shanguier,anmi,missile228,",
    "looking_at_viewer, curvy,seductive_smile,glamor,makeup,blush,, lace,ribbon,jewelry,necklace,drop earrings,pendant,, sexually suggestive,",
    # ----------------------------------------------------
    # 第二行
    "sexy and cute,",
    # ----------------------------------------------------
    # 第三行
    "dynamic pose, sexy pose,",
    # ----------------------------------------------------
    # 第四行 (包含质量标签和角度词)
    r"dynamic angle,, dutch_angle, tinker bell \(pixiv 10956015\),, masterpiece, best quality, amazing quality, very awa,absurdres,newest,very aesthetic,depth of field,",
    "very awa,absurdres,newest,very aesthetic,depth of field,",
]
# ------------------------------------------------------


def log_error(message: str):
    """
    记录错误信息到控制台和日志文件。
    
    注意: 最终应根据用户要求使用 loguru/logger_obj 进行隐性注入。
    """
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    log_entry = f"{timestamp} - {message}\n"
    print(message) # 在控制台打印错误信息
    with open("image_scan_error.log", "a", encoding="utf-8") as log_file:
        log_file.write(log_entry)


def custom_warning_formatter(message, category, filename, lineno, file=None, line=None):
    """
    自定义警告格式化器，尝试获取当前处理的文件路径。
    """
    global _current_processing_file
    
    # 检查警告是否来自 PIL 的 TiffImagePlugin 并且是 Truncated File Read
    if category is UserWarning and "Truncated File Read" in str(message) and "TiffImagePlugin.py" in filename:
        if _current_processing_file:
            return f"UserWarning: {message} for file: '{_current_processing_file}'\n"
    
    # 对于其他警告，使用默认格式
    return warnings.formatwarning(message, category, filename, lineno, line)

# 设置自定义警告格式化器
warnings.formatwarning = custom_warning_formatter


def process_single_image(absolute_path: str) -> Dict[str, Any] | None:
    """
    处理单个图片文件，提取元数据并返回结构化数据。
    此函数设计为独立运行，用于多进程并行处理。
    """
    global _current_processing_file # 声明使用全局变量

    image_extensions = ('.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp')
    
    # 确保文件存在且是图片扩展名
    if not os.path.exists(absolute_path) or not absolute_path.lower().endswith(image_extensions):
        return None # 不是图片或文件不存在，返回None
    
    # 定义一个更通用的正则表达式，用于从原始文本中捕获 Stable Diffusion 的信息块
    sd_full_info_pattern = re.compile(
        r'.*?(?:masterpiece|score_\d|1girl|BREAK|Negative prompt:|Steps:).*?(?:Version:.*?|Module:.*?|)$',
        re.DOTALL # 允许.匹配换行符
    )
    # 定义一个更严格的正则，用于最终验证是否是有效的SD参数
    sd_validation_pattern = re.compile(r'Steps: \d+, Sampler: [\w\s]+', re.DOTALL)
    
    # 初始化变量
    containing_folder_absolute_path = os.path.abspath(os.path.dirname(absolute_path))
    sd_info = "没有扫描到生成信息"
    sd_info_no_newlines = "没有扫描到生成信息"
    positive_prompt = ""
    negative_prompt = ""
    other_settings = ""
    model_name = "未找到模型"
    positive_prompt_word_count = 0
    raw_metadata_string = ""
    creation_date_dir = "未获取日期"
    core_positive_prompt = "核心词为空" # 新增核心词变量

    _current_processing_file = absolute_path # 在处理每个文件前更新全局变量

    try:
        # --- 获取文件创建日期 ---
        try:
            # 使用os.path.getctime()获取时间戳
            creation_time = os.path.getctime(absolute_path)
            dt_object = datetime.fromtimestamp(creation_time)
            creation_date_dir = dt_object.strftime("%Y-%m-%d")
        except Exception:
            # 这里的错误不严重，不使用log_error，仅设置默认值
            pass 
        
        # --- 开始图像元数据提取 ---
        with Image.open(absolute_path) as img:
            # --- 阶段 1: 尝试从标准位置获取原始元数据字符串 ---
            if "png" in img.format.lower() and "parameters" in img.info:
                raw_metadata_string = img.info["parameters"]
            elif "jpeg" in img.format.lower() or "webp" in img.format.lower(): # 兼容 jpeg 和 webp
                if hasattr(img, '_getexif'):
                    exif_data = img._getexif()
                    if exif_data:
                        for tag, value in exif_data.items():
                            if tag in [0x9286, 0x010E]: # UserComment (0x9286) or ImageDescription (0x010E)
                                try:
                                    if isinstance(value, bytes):
                                        raw_metadata_string = value.decode('utf-8', errors='ignore')
                                        if not re.search(r'Steps:', raw_metadata_string):
                                            raw_metadata_string = value.decode('latin-1', errors='ignore')
                                    elif isinstance(value, str):
                                        raw_metadata_string = value
                                    break
                                except Exception:
                                    pass
            
            # --- 阶段 2: 清理并使用更强大的正则表达式提取有效信息 ---
            if isinstance(raw_metadata_string, str) and raw_metadata_string:
                # 移除 Excel 不支持的非法 XML 字符
                cleaned_string = ILLEGAL_CHARACTERS_RE.sub(r'', raw_metadata_string)
                
                # Clean up the "UNICODE" prefix
                if cleaned_string.startswith("UNICODE"):
                    cleaned_string = cleaned_string[len("UNICODE"):].lstrip() # Remove "UNICODE" and any leading whitespace
                
                # 尝试使用新的正则表达式捕获核心SD信息块
                match = sd_full_info_pattern.search(cleaned_string)
                
                if match:
                    extracted_text = match.group(0).strip() # 获取匹配到的整个SD信息块
                    # 再次使用更严格的正则验证，确保提取的是有效的SD参数
                    if sd_validation_pattern.search(extracted_text):
                        sd_info = extracted_text
                        # 新增：生成没有换行符的生成信息
                        sd_info_no_newlines = sd_info.replace('\n', ' ').replace('\r', ' ').strip()
                        
                        # --- 阶段 3: 切割信息 (现在从 sd_info_no_newlines 切割) ---
                        # 从后往前切割
                        other_settings_match = re.search(r'(Steps:.*)', sd_info_no_newlines, re.DOTALL)
                        if other_settings_match:
                            other_settings = other_settings_match.group(1).strip()
                            temp_sd_info = sd_info_no_newlines[:other_settings_match.start()].strip()
                        else:
                            temp_sd_info = sd_info_no_newlines.strip()

                        negative_prompt_match = re.search(r'(Negative prompt:.*?)(?=\s*Steps:|$)', temp_sd_info, re.DOTALL)
                        if negative_prompt_match:
                            negative_prompt = negative_prompt_match.group(1).replace("Negative prompt:", "").strip()
                            positive_prompt = temp_sd_info[:negative_prompt_match.start()].strip()
                        else:
                            positive_prompt = temp_sd_info.strip()
                        
                        # 统计正面提示词字数
                        positive_prompt_word_count = len(positive_prompt)

                    else:
                        sd_info = "没有扫描到生成信息"
                        sd_info_no_newlines = "没有扫描到生成信息"
                else:
                    sd_info = "没有扫描到生成信息"
                    sd_info_no_newlines = "没有扫描到生成信息"

            # --- 阶段 4: 提取正向提示词的核心词 (新增功能) ---
            core_positive_prompt = positive_prompt
            # 将所有停用词替换为空字符串
            for word in POSITIVE_PROMPT_STOP_WORDS:
                
                # 为确保替换词两边有空格，我们先给 core_positive_prompt 两边加空格
                core_positive_prompt = f" {core_positive_prompt} "
                
                # 替换，忽略大小写
                core_positive_prompt = re.sub(
                    re.escape(word), # 需要转义以处理括号等特殊字符
                    " ",             # 替换为空格，避免粘连
                    core_positive_prompt,
                    flags=re.IGNORECASE # 忽略大小写匹配
                )

            # 3. 清理结果：移除多余的空格和首尾空格
            core_positive_prompt = core_positive_prompt.strip()
            # 移除所有连续的空格，只保留一个
            core_positive_prompt = re.sub(r'\s+', ' ', core_positive_prompt)
            
            # 如果清理后为空，则设置为提示信息
            if not core_positive_prompt:
                core_positive_prompt = "核心词为空"
                
            # 从 other_settings 中提取 Model 信息
            model_match = re.search(r'Model: ([^,]+)', other_settings)
            if model_match:
                model_name = model_match.group(1).strip()


    except Exception as e:
        # 如果Image.open()或后续操作因文件损坏而失败，这里的e会包含详细错误信息
        log_error(f"Error processing image file '{absolute_path}' : {e}") # 明确指出是哪个文件出了问题
        # 发生任何错误时都保持默认值
    finally:
        _current_processing_file = None # 处理完一个文件后重置全局变量

    # 返回结果字典
    return {
        "所在文件夹": containing_folder_absolute_path,
        "图片的绝对路径": absolute_path,
        "图片超链接": f'={absolute_path}',
        "stable diffusion的 ai图片的生成信息": sd_info,
        "去掉换行符的生成信息": sd_info_no_newlines, # 新增列
        "正面提示词": positive_prompt,
        "负面提示词": negative_prompt,
        "其他设置": other_settings,
        "正面提示词字数": positive_prompt_word_count, # 新增列
        "模型": model_name, # 新增列
        "创建日期目录": creation_date_dir, # 新增列
        "提取正向词的核心词": core_positive_prompt # 新增列
    }

def get_image_info(folder_path: str) -> List[Dict[str, Any]]:
    """
    (多进程优化) 扫描文件夹获取所有图片路径，并使用进程池并行提取元数据。
    
    :param folder_path: 要扫描的根目录路径。
    :return: 包含所有图片元数据字典的列表。
    """
    image_paths = []
    image_extensions = ('.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp')

    # 1. 阶段：单线程快速收集所有图片路径
    for root, dirs, files in os.walk(folder_path):
        
        # [2025-10-27] 新增: 排除名为 '.bf' 的文件夹
        # os.walk 的 dirs 列表可以直接修改，以跳过对子目录的递归
        if '.bf' in dirs:
            print(f"警告: 发现并跳过文件夹: {os.path.join(root, '.bf')}")
            dirs.remove('.bf')
            
        for file in files:
            if file.lower().endswith(image_extensions):
                image_paths.append(os.path.abspath(os.path.join(root, file)))

    if not image_paths:
        return []
    
    image_data = []
    
    # 2. 阶段：多进程并行处理每个图片文件
    print(f"检测到 {len(image_paths)} 个图片文件。使用 {MAX_WORKERS} 个进程并行扫描元数据...")
    
    # [2025-10-31] 新增：计数器，用于统计成功和失败
    success_count = 0
    failure_count = 0
    
    with concurrent.futures.ProcessPoolExecutor(max_workers=MAX_WORKERS) as executor:
        # 使用 executor.map 将所有文件路径映射到 process_single_image 函数
        results = executor.map(process_single_image, image_paths)
        
        # 3. 阶段：收集和过滤结果 (使用 tqdm 包装结果进行进度条展示)
        # [2025-10-31] 新增: 使用 tqdm 实现任务实时预览/计数器
        for result in tqdm(results, total=len(image_paths), desc="扫描图片元数据"):
            # 过滤掉返回 None 的结果 (非图片或路径问题)
            if result:
                # 只要 result 不是 None，就将其添加到数据列表中。
                image_data.append(result)
                success_count += 1 # 成功获取元数据
            else:
                failure_count += 1 # 失败/跳过 (非图片、路径不存在等)

    # 4. 阶段：打印最终计数器日志 (符合用户要求)
    print("\n--- 元数据扫描计数器总结 ---")
    print(f"总数量: {len(image_paths)}, 成功: {success_count}, 失败/跳过: {failure_count}")

    return image_data

# 注意: 此模块不包含 __main__ 块，因为它是一个工具函数模块