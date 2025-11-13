# -*- coding: utf-8 -*-
import os
import re
from typing import List, Dict, Any, Optional # 导入 Optional
from datetime import datetime
from tqdm import tqdm # 导入 tqdm, 用于进度条显示

# 默认禁止自动分类的文件夹名称列表 (完整匹配)
# 位于这些文件夹内的文件将不会被 categorize_images 移动
DEFAULT_PROTECTED_FOLDER_NAMES = [
    "超级精选",
    "超绝",
    "精选",
    "特殊画风",
]

# [2025-10-29 新增] 模糊匹配关键词列表
# 如果文件夹名称中包含这些关键词，也视为受保护文件夹 (例如: "我的特殊收藏"会被保护)
FUZZY_PROTECTED_KEYWORDS = [
    "特殊",
    "精选",
    "手动",
]

# 依赖导入：从 filename_tagger 模块导入冲突解决函数
try:
    from filename_tagger import get_unique_filename
except ImportError:
    # 占位函数，避免模块缺失时代码崩溃
    def get_unique_filename(target_dir, filename):
        print(f"[ERROR] filename_tagger.py 缺失，归档冲突解决失败，返回原始文件名: {filename}")
        return filename
    print("警告: 无法导入 filename_tagger.get_unique_filename。文件冲突解决功能将失效。")


def log_error(message: str):
    """
    [临时] 记录错误信息到控制台和日志文件。
    
    注意: 最终应根据用户要求使用 loguru/logger_obj 进行隐性注入。
    """
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    log_entry = f"{timestamp} - {message}\n"
    print(message) # 在控制台打印错误信息
    # 假设日志文件名为 image_scan_error.log
    with open("image_scan_error.log", "a", encoding="utf-8") as log_file:
        log_file.write(log_entry)


def merge_and_move_folder(source_dir: str, target_dir: str) -> bool:
    """
    递归地将源文件夹内容合并到目标文件夹中。
    文件如果已存在则重命名添加 (N) 后移动。
    
    [2025-10-28] 依赖: 外部函数 get_unique_filename (来自 filename_tagger 模块)
    
    :param source_dir: 源目录路径 (将被清空或删除)
    :param target_dir: 目标目录路径
    :return: bool, 源文件夹是否被成功删除/清空
    """
    content_moved = False
    
    for item in os.listdir(source_dir):
        source_path = os.path.join(source_dir, item)
        target_path = os.path.join(target_dir, item)
        
        if os.path.isdir(source_path):
            # 如果是子文件夹
            if not os.path.exists(target_path):
                # 目标不存在，直接移动整个文件夹
                os.rename(source_path, target_path)
                content_moved = True
            else:
                # 目标存在，递归合并
                sub_folder_moved = merge_and_move_folder(source_path, target_path)
                if sub_folder_moved:
                    content_moved = True # 即使是递归移动也算作内容有移动
        elif os.path.isfile(source_path):
            # 如果是文件
            if not os.path.exists(target_path):
                # 目标不存在，直接移动文件
                os.rename(source_path, target_path)
                content_moved = True
            else:
                # 目标已存在同名文件，调用 get_unique_filename 解决冲突
                original_filename = item
                # 调用从 filename_tagger 导入的函数
                unique_filename = get_unique_filename(target_dir, original_filename) 
                
                new_target_path = os.path.join(target_dir, unique_filename)
                
                # 重命名并移动文件
                try:
                    os.rename(source_path, new_target_path)
                    content_moved = True
                    log_error(f"    提醒: 文件冲突解决，已重命名移动 '{original_filename}' -> '{unique_filename}'") # 使用 log_error 记录警告
                except Exception as e:
                    log_error(f"警告: 归档合并时重命名移动文件 '{source_path}' 失败: {e}")


    # 递归结束后，尝试删除源文件夹（如果它现在是空的）
    if not os.listdir(source_dir):
        try:
            os.rmdir(source_dir)
            return True # 源文件夹已被删除，表示所有内容已成功转移/处理
        except OSError as e:
            log_error(f"错误: 无法删除空文件夹 '{source_dir}'。请手动检查权限。错误: {e}")
            return False
    return False # 源文件夹非空，或删除失败


def archive_date_folders(base_source_dir: str, archive_target_dir: str):
    """
    遍历 base_source_dir 下的一级子文件夹，如果文件夹名称是 YYYY-MM-DD 格式，
    则将其整个移动归档到 archive_target_dir 文件夹下。
    
    :param base_source_dir: 扫描源目录 (如: .../txt2img-images/)
    :param archive_target_dir: 归档目标目录 (如: .../txt2img-images/历史)
    """
    print("\n--- 开始执行日期文件夹归档操作 ---")
    
    # 确保目标目录存在
    if not os.path.exists(archive_target_dir):
        try:
            os.makedirs(archive_target_dir)
            print(f"创建归档目标目录: {archive_target_dir}")
        except Exception as e:
            log_error(f"创建归档目标目录失败: {e}")
            return # 创建失败则退出归档

    # 日期文件夹名称的正则表达式 (YYYY-MM-DD)
    date_folder_pattern = re.compile(r'^\d{4}-\d{2}-\d{2}$')
    
    total_found = 0
    total_archived = 0

    try:
        # 获取绝对路径用于安全比较
        abs_archive_target_dir = os.path.abspath(archive_target_dir)
        
        for item in os.listdir(base_source_dir):
            source_path = os.path.join(base_source_dir, item)
            
            # 1. 检查是否是文件夹
            if os.path.isdir(source_path):
                # 2. 检查文件夹名称是否符合 YYYY-MM-DD 格式
                if date_folder_pattern.match(item):
                    
                    # 3. 排除归档目标目录本身（绝对路径比较）
                    if os.path.abspath(source_path) == abs_archive_target_dir:
                        print(f"跳过归档目标目录本身: {item}")
                        continue
                        
                    total_found += 1
                    target_path = os.path.join(archive_target_dir, item)
                    
                    if not os.path.exists(target_path):
                        # 目标位置不存在，直接移动整个文件夹
                        os.rename(source_path, target_path)
                        print(f"成功归档(移动)文件夹: '{item}' -> '{archive_target_dir}'")
                        total_archived += 1
                    else:
                        # 目标位置已存在同名文件夹，执行合并操作
                        print(f"提醒: 归档目标位置已存在同名文件夹 '{item}'。开始合并内容...")
                        # 核心修改：调用合并函数，并检查是否成功清理了源文件夹
                        source_folder_empty = merge_and_move_folder(source_path, target_path)
                        
                        if source_folder_empty:
                            # merge_and_move_folder成功后会自行删除源文件夹
                            print(f"成功归档(合并)并清理源文件夹: {item}")
                            total_archived += 1 # 视为成功归档（合并也是归档的一种）
                        else:
                            # 源文件夹合并后仍有内容
                            print(f"提醒: 文件夹 '{item}' 合并操作完成，但源文件夹可能仍有内容或清理失败。")
                            
    except Exception as e:
        log_error(f"日期文件夹归档时发生错误: {e}")
        
    print(f"日期文件夹归档操作完成。发现日期文件夹 {total_found} 个，成功归档(移动或合并) {total_archived} 个。")


def categorize_images(
    image_data: List[Dict[str, Any]], 
    keyword_list: List[str], 
    root_dir_path: str, 
    protected_folders: Optional[List[str]] = None,
    additional_protected_names: Optional[List[str]] = None, # [2025-10-28 新增] 额外的受保护文件夹名称列表
) -> List[Dict[str, str]]: # [修改] 新增返回类型
    """
    根据创建日期和关键词列表，将图片进行两级分类和移动。
    第一级: 创建日期 (YYYY-MM-DD)
    第二级: 关键词或 '未分类'
    
    :param image_data: 包含图片信息的列表。
    :param keyword_list: 用于文件夹分类的关键词列表。
    :param root_dir_path: 扫描的主文件夹路径，也是分类的根目录。
    :param protected_folders: 受保护的文件夹绝对路径列表，位于其中的文件不会被移动。
    :param additional_protected_names: [2025-10-28 新增] 额外禁止自动分类的文件夹名称列表 (如: 精选)。
    :return: List[Dict[str, str]], 包含分类操作记录的列表。 # [修改] 新增返回描述
    """
    if not image_data:
        print("没有图片数据可供分类，跳过分类操作。")
        return [] # 返回空列表

    print("\n--- 开始图片两级分类操作 (日期/关键词) ---")
    total_images = len(image_data)
    # [移除旧计数器] classified_count = 0 
    # [移除旧计数器] unclassified_count = 0 
    
    # [新增] 操作日志列表
    classification_log: List[Dict[str, str]] = []
    
    # 确保根目录是绝对路径
    root_dir = os.path.abspath(root_dir_path)

    # 1. 构建完整的受保护文件夹名称集合 (完整匹配 + 外部传入)
    protected_names = set(DEFAULT_PROTECTED_FOLDER_NAMES)
    if additional_protected_names:
        protected_names.update(additional_protected_names)
        
    # 2. [核心新增] 将模糊匹配的关键词转换为小写，用于进行 IN 检查
    lower_fuzzy_keywords = [kw.lower() for kw in FUZZY_PROTECTED_KEYWORDS] #

    # 将关键词转为小写，以便进行不区分大小写的匹配
    lower_keyword_list = [kw.strip().lower() for kw in keyword_list if kw.strip()]
    if not lower_keyword_list:
        print("分类关键词列表为空，将全部移动到日期/未分类文件夹。")
        
    for i, data in enumerate(image_data):
        current_image_path = data["图片的绝对路径"]
        image_dir = os.path.dirname(current_image_path) # 当前图片所在的文件夹
        image_dir_name = os.path.basename(image_dir) # 当前图片所在文件夹的名称
        image_filename = os.path.basename(current_image_path)
        image_dir_name_lower = image_dir_name.lower() # [新增] 文件夹名称小写，用于模糊匹配

        # [新增] 初始日志记录 (在跳过前记录，方便追踪)
        log_entry: Dict[str, str] = {
            "图片文件名": image_filename,
            "初始绝对路径": current_image_path,
            "结束绝对路径": current_image_path, # 默认值，如果跳过则不变
            "状态类型": "未处理 (初始化)", 
        }


        # [2025-10-29 核心修改] 安全检查 (按文件夹名称跳过): 
        # 优先级 1: 完整名称匹配
        if image_dir_name in protected_names:
            log_error(f"【安全跳过】文件 '{image_filename}' 位于名称受保护文件夹 (完整匹配) '{image_dir_name}'。跳过分类和移动。")
            log_entry["状态类型"] = "安全跳过 (保护路径)" # 统一标记安全跳过
            classification_log.append(log_entry) # 记录日志
            continue # 跳过当前文件的处理
        
        # 优先级 2: 模糊关键词匹配
        # 检查文件夹名称是否包含任何模糊保护关键词
        if any(keyword in image_dir_name_lower for keyword in lower_fuzzy_keywords): #
            log_error(f"【安全跳过】文件 '{image_filename}' 位于名称受保护文件夹 (模糊匹配) '{image_dir_name}'。跳过分类和移动。")
            log_entry["状态类型"] = "安全跳过 (保护路径)" # 统一标记安全跳过
            classification_log.append(log_entry) # 记录日志
            continue # 跳过当前文件的处理

        # [2025-10-28] 原有安全检查 (按文件夹绝对路径跳过): 
        image_dir_abs = os.path.abspath(image_dir)
        if protected_folders and image_dir_abs in protected_folders:
            # 记录 log_error 方便调试
            log_error(f"【安全跳过】文件 '{image_filename}' 位于绝对路径受保护文件夹 '{image_dir_abs}'。跳过分类和移动。")
            log_entry["状态类型"] = "安全跳过 (保护路径)" # 统一标记安全跳过
            classification_log.append(log_entry) # 记录日志
            continue # 跳过当前文件的处理
        
        # 1. 第一级分类：确定日期文件夹 (YYYY-MM-DD 或 未获取日期)
        date_dir_name = data.get("创建日期目录", "未获取日期")
        date_folder = os.path.join(root_dir, date_dir_name) # 第一级目录路径

        # 2. 第二级分类：确定关键词分类结果
        positive_prompt = data.get("正面提示词", "").lower()
        negative_prompt = data.get("负面提示词", "").lower()
        all_prompts = f"{positive_prompt} {negative_prompt}"
        
        target_keyword = None
        is_classified = False
        
        # 优先级匹配：严格按照 keyword_list 的顺序
        for keyword in lower_keyword_list:
            if keyword in all_prompts:
                # 找到匹配，确定目标关键词（使用原始大小写）
                original_keyword = keyword_list[lower_keyword_list.index(keyword)] 
                target_keyword = original_keyword
                is_classified = True
                break
        
        # 3. 确定最终目标文件夹 (第二级目录)
        target_sub_folder = None
        if is_classified:
            target_sub_folder = target_keyword # 关键词文件夹
        else:
            target_sub_folder = "未分类" # 未匹配则归入未分类

        # 最终目标路径：根目录 / 日期目录 / 关键词或未分类目录
        target_folder = os.path.join(date_folder, target_sub_folder) 

        # 4. 确定理想目标路径 (不进行冲突解决)
        # 理想目标路径：根目录 / 日期目录 / 关键词或未分类目录 / 文件名
        ideal_target_path = os.path.join(target_folder, image_filename) # 【新增】计算理想路径
        
        # 5. 【核心I/O优化】检查当前位置是否等于理想目标位置 (Idempotency Check)
        # 如果文件已经位于正确的目标文件夹，并且文件名是理想的文件名（未添加 (N)）
        if current_image_path == ideal_target_path:
            # 如果目标路径和当前路径相同，则无需移动，跳过I/O操作
            if is_classified:
                log_entry["状态类型"] = "因路径相同而跳过I/O (已在关键词目录)"
                print(f"无需移动 ({i+1}/{total_images}): '{image_filename}' 已位于目标位置 '{date_dir_name}/{target_keyword}'。")
            else:
                log_entry["状态类型"] = "因路径相同而跳过I/O (已在未分类目录)"
                print(f"无需移动 ({i+1}/{total_images}): '{image_filename}' 已位于目标位置 '{date_dir_name}/未分类'。")
            
            classification_log.append(log_entry) # 记录日志
            continue # 跳到下一个文件
            
        # 6. 确定最终目标路径 (执行冲突解决)
        # 只有在不匹配理想目标路径时，才执行冲突解决
        # 使用 get_unique_filename 检查分类目标文件夹是否存在同名文件，避免冲突
        unique_filename = get_unique_filename(target_folder, image_filename) 
        new_image_path = os.path.join(target_folder, unique_filename)

        # 7. 移动操作 (当且仅当目标路径和当前路径不一致时才执行)
        try:
            # 创建目标子文件夹 (包括日期文件夹和关键词/未分类文件夹)
            if not os.path.exists(target_folder):
                os.makedirs(target_folder)
            
            # 执行重命名/移动
            os.rename(current_image_path, new_image_path)
            
            # 统计和日志
            if is_classified:
                log_entry["状态类型"] = "成功分类到关键词目录"
                print(f"成功分类 ({i+1}/{total_images}): '{image_filename}' -> '{date_dir_name}/{target_keyword}'")
            else: 
                log_entry["状态类型"] = "成功移入/保留在 '未分类' 目录"
                print(f"移入未分类 ({i+1}/{total_images}): '{image_filename}' -> '{date_dir_name}/未分类'")
                
            classification_log.append(log_entry) # 记录成功移动的日志
                
            # 8. 清理空闲原文件夹
            # 只要原文件夹不是最顶层的主文件夹 (root_dir)，且移动后变空，就尝试清理
            if os.path.abspath(image_dir) != root_dir and not os.listdir(image_dir): # 使用 os.path.abspath 确保比较的是同一路径
                try:
                    os.rmdir(image_dir)
                    print(f"清理空文件夹: {image_dir}")
                except OSError as e:
                    log_error(f"警告: 无法清理空文件夹 '{image_dir}'，可能仍有隐藏文件。错误: {e}")
                
        except Exception as e:
            log_entry["状态类型"] = f"移动失败/其他异常: {e}" # 统一失败状态
            classification_log.append(log_entry)
            log_error(f"分类/移动文件 '{current_image_path}' 时发生错误: {e}")

    # [替换] 最终统计日志：从生成的 classification_log 中计算
    if classification_log:
        status_counts = {}
        # 遍历日志，统一状态类型并计数
        for entry in classification_log:
            status = entry['状态类型']
            if status.startswith("因路径相同而跳过I/O"):
                key = "因路径相同而跳过I/O"
            elif status.startswith("成功分类到关键词目录"):
                key = "成功分类到关键词目录"
            elif status.startswith("成功移入/保留在 '未分类' 目录"):
                key = "成功移入/保留在 '未分类' 目录"
            elif status.startswith("安全跳过 (保护路径)"):
                key = "安全跳过 (保护路径)"
            elif status.startswith("移动失败"):
                key = "移动失败/其他异常"
            else:
                key = "其他未识别状态"
                
            status_counts[key] = status_counts.get(key, 0) + 1
            
        # 提取最终统计结果
        classified_count_final = status_counts.get("成功分类到关键词目录", 0)
        unclassified_count_final = status_counts.get("成功移入/保留在 '未分类' 目录", 0)
        safe_skipped_count_final = status_counts.get("安全跳过 (保护路径)", 0)
        io_skipped_count_final = status_counts.get("因路径相同而跳过I/O", 0)
        failed_count_final = status_counts.get("移动失败/其他异常", 0)

        # 打印最终统计结果 (满足用户要求的格式)
        print(f"\n--- 图片两级分类操作完成 ---")
        print(f"总共处理图片: {total_images} 张")
        print(f"因路径相同而跳过I/O: {io_skipped_count_final} 张")
        print(f"【安全跳过】的图片数量: {safe_skipped_count_final} 张")
        print(f"成功分类到关键词目录: {classified_count_final} 张")
        print(f"成功移入/保留在 '未分类' 目录: {unclassified_count_final} 张")
        print(f"移动失败/其他异常: {failed_count_final} 张")
        
        # 验证总数
        calculated_total = (classified_count_final + unclassified_count_final + safe_skipped_count_final + io_skipped_count_final + failed_count_final)
        if calculated_total != total_images:
             log_error(f"警告: 最终统计总数 ({calculated_total}) 不等于初始图片总数 ({total_images})。存在未记录状态。")
        
    return classification_log # 返回操作日志列表