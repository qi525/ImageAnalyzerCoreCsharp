# -*- coding: utf-8 -*-
import os
import re
from typing import List, Dict, Any

# [2025-10-28] 新增: 用于文件名标记的关键词列表 (已从 getIMGINFOandClassify.py 分离)
TAGGING_KEYWORDS: List[str] = [
    "shorekeeper",
    "noshiro","rio_(blue_archive)","taihou","azur_lane","blue_archive","fgo","pokemon","fate","touhou","idolmaster","love_live","bleach","gundam","umamusume","honkai","hololive","one_piece","final_fantasy","persona","zelda","chainsaw_man","nikke","xenoblade","kantai_collection","genshin_impact","love_live","naruto","overwatch","genderswap","futanari", "skeleton", "green_hair","splatoon","boku_no_hero_academia","midoriya_izuku","ashido_mina","band-aid","covered_nipples","undressing","removing_bra","tail_around_neck","asphyxiation","strangling",
    "pasties",           # 乳贴/胸贴
    "cross_pasties",     # 十字乳贴
    "tape",              # 胶带
    "tape_on_nipples",   # 胶带贴在乳头上
    "open_clothes",      # 敞开的衣服
    "open_jacket",       # 敞开的夹克
    "no_pants"           # 没穿裤子
]

def get_unique_filename(target_dir, filename):
    """
    根据目标文件夹和文件名，生成一个不冲突的唯一文件名。
    如果文件已存在，则在文件名末尾添加 (1), (2), (3)...

    :param target_dir: 目标文件夹路径。
    :param filename: 原始文件名。
    :return: 唯一的、不冲突的文件名。
    """
    base, ext = os.path.splitext(filename)
    new_filename = filename
    counter = 1
    
    # 检查目标路径是否已存在该文件
    while os.path.exists(os.path.join(target_dir, new_filename)):
        # 尝试移除文件末尾可能存在的 (N) 标记
        match = re.search(r'\(\d+\)$', base) 
        if match:
             # 如果基础名称已包含 (N) 标记，则只更新数字
            base = base[:match.start()]
            new_filename = f"{base}({counter}){ext}"
        else:
            # 如果没有 (N) 标记，则添加 (N) 标记
            # 这里的 base 已经是原始文件名 (不含扩展名)
            new_filename = f"{base}({counter}){ext}"
        
        counter += 1
        
        if counter > 1000: # 避免无限循环的保护措施
            # 为了保持模块的独立性，这里使用print/raise而不是log_error
            raise Exception(f"无法为文件 '{filename}' 在目标目录 '{target_dir}' 中找到唯一名称。")

    return new_filename


def _parse_base_filename(filename: str, tag_delimiter: str = "___") -> tuple[str, str]:
    """
    从带有标签后缀的文件名中，安全地提取原始基础文件名（不含标签和扩展名）。
    
    例如: "image_base___tag1___tag2.png" -> ("image_base", ".png")

    :param filename: 包含扩展名和可能包含标签后缀的文件名。
    :param tag_delimiter: 用于分隔 tag 的字符串。
    :return: (base_name_without_tags, ext)
    """
    base, ext = os.path.splitext(filename)
    
    # 构建匹配标签后缀的正则表达式: 匹配定界符开头，后面跟着非定界符或定界符本身，直到字符串末尾。
    tag_pattern = re.compile(f'({re.escape(tag_delimiter)}.+?)$')
    
    match = tag_pattern.search(base)
    
    if match:
        # 如果找到标签后缀，则返回不包含标签的部分
        base_name_without_tags = base[:match.start()]
        # 确保 base_name_without_tags 不为空，如果为空（整个文件名都是标签），则返回原 base
        if base_name_without_tags:
            return base_name_without_tags, ext
    
    # 如果没有找到匹配的标签后缀，则返回原文件名（不含扩展名）
    return base, ext

def tag_files_by_prompt(image_data: List[Dict[str, Any]], keyword_list: List[str], tag_delimiter: str = "___", tfidf_suffix_col: str = None) -> List[Dict[str, Any]]:
    """
    增强功能：根据提示词匹配关键词，给文件添加或更新 '___tag1___tag2' 后缀，
    并**附加** TF-IDF 分析得到的关键词后缀。

    :param image_data: 包含图片信息的列表。
    :param keyword_list: 用于匹配的关键词列表（按顺序）。
    :param tag_delimiter: 用于分隔 tag 的字符串，默认为 "___"。
    :param tfidf_suffix_col: 包含预先计算的 TF-IDF 后缀字符串的列名 (如 'TF-IDF文件名后缀')。
    :return: 更新后的 image_data 列表。
    """
    # ⚠️ 错误处理简化：由于 log_error 是外部函数，在此独立模块中用 print 代替
    def simple_error_log(message):
        print(f"[ERROR] {message}")

    print("\n--- 开始执行文件名标记操作 (添加关键词后缀，并重置旧标签) ---")
    
    # [新增] 检查 TF-IDF 附加列是否启用
    tfidf_suffix_enabled = tfidf_suffix_col and image_data and tfidf_suffix_col in image_data[0]
    if tfidf_suffix_enabled:
        print(f"文件名标记将额外附加来自列 '{tfidf_suffix_col}' 的 TF-IDF 后缀。")
    print(f"当前文件名标记关键词列表: {keyword_list} (定界符: {tag_delimiter} )")
    
    if not image_data:
        print("没有图片数据可供标记，跳过操作。")
        return []

    total_images = len(image_data)
    renamed_count = 0
    
    # 转换为小写，方便不区分大小写的匹配
    lower_keyword_list = [kw.strip().lower() for kw in keyword_list if kw.strip()]
    
    for i, data in enumerate(image_data):
        current_image_path = data["图片的绝对路径"]
        image_dir = os.path.dirname(current_image_path)
        image_filename = os.path.basename(current_image_path)
        
        # 提示词预处理
        positive_prompt = data.get("正面提示词", "").lower()
        negative_prompt = data.get("负面提示词", "").lower()
        
        # 消除WebUI可能自动添加的转义符 (\)
        positive_prompt_clean = positive_prompt.replace("\\", "")
        negative_prompt_clean = negative_prompt.replace("\\", "")
        
        all_prompts = f"{positive_prompt_clean} {negative_prompt_clean}"
        
        matched_tags = []
        # 1. 严格按顺序匹配关键词 (非 TF-IDF 关键词)
        for keyword in lower_keyword_list:
            # 使用原始大小写的关键词作为最终 tag
            original_keyword = keyword_list[lower_keyword_list.index(keyword)] 
            if keyword in all_prompts:
                matched_tags.append(original_keyword)
        
        # 2. 构建提示词匹配关键词的后缀 tag 字符串
        tag_suffix = ""
        if matched_tags:
            # 格式：___tag1___tag2___tag3...
            tag_suffix = tag_delimiter + tag_delimiter.join(matched_tags)
            
        # 3. [新增] 获取 TF-IDF 后缀字符串
        tfidf_suffix = ""
        if tfidf_suffix_enabled:
            # 从数据字典中获取预先计算好的 TF-IDF 后缀 (例如: ___tagA___tagB)
            tfidf_suffix = data.get(tfidf_suffix_col, "")
        
        # 4. 组合最终后缀：提示词匹配关键词后缀 + TF-IDF 后缀
        # 格式: (___tag1___tag2) + (___tagA___tagB)
        final_suffix = tag_suffix + tfidf_suffix 

        # 5. 解析原始基础文件名 (不含任何标签后缀)
        base_name_without_tags, ext = _parse_base_filename(image_filename, tag_delimiter) 
        
        # 6. 确定新的完整文件名 (使用干净的基础名 + 最终后缀)
        new_base_name = base_name_without_tags + final_suffix # 核心文件名 + 最终后缀
        new_filename = new_base_name + ext
        new_image_path = os.path.join(image_dir, new_filename)

        # 7. 【幂等性保护机制】检查是否已经等于目标文件名
        if current_image_path == new_image_path:
             print(f"无需标记 ({i+1}/{total_images}): '{image_filename}' 标签已精确匹配，跳过重命名。")
             continue 
        
        # 8. 执行重命名操作
        try:
            if os.path.exists(new_image_path):
                # 目标文件名已存在，可能是手动改名或罕见冲突
                simple_error_log(f"警告: 目标文件名 '{new_filename}' 已存在，跳过重命名以避免冲突。")
                continue
            
            os.rename(current_image_path, new_image_path)
            
            # 9. 更新 image_data 中的路径信息
            data["图片的绝对路径"] = new_image_path
            data["图片超链接"] = f'={new_image_path}'
            
            renamed_count += 1
            # 日志输出
            print(f"成功标记/更新 ({i+1}/{total_images}): '{image_filename}' -> '{new_filename}'")
            
        except Exception as e:
            simple_error_log(f"重命名文件 '{current_image_path}' 时发生错误: {e}")

    print(f"\n文件名标记操作完成。总共处理图片 {total_images} 张，成功重命名 {renamed_count} 张。")
    return image_data