import pandas as pd
from sklearn.feature_extraction.text import TfidfVectorizer
from loguru import logger # 默认使用 loguru 进行日志记录
import re
from typing import List, Tuple, Dict, Any
import numpy as np # 用于处理 TF-IDF 矩阵
import concurrent.futures # 导入多进程模块，用于并行加速计算
import os # 用于获取 CPU 核心数
import datetime # 用于实现计数器的时间跟踪
from tqdm import tqdm # 导入 tqdm 用于进度条
# @@    9-9,10-10   @@ 新增: 导入 tqdm 用于进度条


def preprocess_tags(tags_series: pd.Series) -> Tuple[List[str], pd.Series]:
# ... (此函数内容不变) ...
    """
    预处理标签数据：将标签文本转换为适合TF-IDF分析的格式。
    
    Args:
        tags_series: 包含原始标签文本的 Series (通常是 '提取正向词的核心词' 列)。

    Returns:
        Tuple[List[str], pd.Series]:
            - corpus: 用于 TF-IDF 计算的有效文档列表 (已去除空值)。
            - cleaned_tags: 经过清洗但包含所有索引的标签 Series。
    """
    try:
        logger.info("开始预处理标签数据...")
    except NameError:
        print("开始预处理标签数据...")
    
    # 确保所有值都是字符串，并处理可能的缺失值
    cleaned_tags = tags_series.fillna('').astype(str).str.lower()
    
    # 替换所有分隔符（逗号, 换行符, 冒号, 括号, 下划线, 连字符）为单个空格
    # 这样可以确保分词器正确识别关键词
    cleaned_tags = cleaned_tags.apply(lambda x: re.sub(r'[\n,:_()\[\]-]+', ' ', x))
    
    # 替换所有逗号和分号为空格，并去除多余的空格
    cleaned_tags = cleaned_tags.str.replace(r'[;,]', ' ', regex=True)
    cleaned_tags = cleaned_tags.str.strip().str.replace(r'\s+', ' ', regex=True)
    
    # 确保没有空的文档被输入到 TF-IDF
    corpus = cleaned_tags[cleaned_tags != ''].tolist()
    
    try:
        logger.info(f"数据预处理完成，共计 {len(corpus)} 条有效记录用于TF-IDF计算。")
    except NameError:
        print(f"数据预处理完成，共计 {len(corpus)} 条有效记录用于TF-IDF计算。")

    return corpus, cleaned_tags


def _extract_top_n_for_single_row(
    args: Tuple[int, np.ndarray, np.ndarray, int]
) -> Tuple[int, Tuple[str, List[str]]]:
# ... (此函数内容不变) ...
    """
    辅助函数：处理单个文档的 TF-IDF 分数，提取 Top N 关键词并格式化。
    此函数在独立的进程中运行，用于并行加速 CPU 密集型任务。
    
    Args:
        args: (original_df_index, doc_scores, feature_names, top_n_features)
    
    Returns:
        Tuple[int, Tuple[str, List[str]]]: 
            - original_df_index: 原始 DataFrame 的索引。
            - result_tuple: (Excel报告字符串, 文件名关键词列表)
    """
    # 解包参数
    original_idx, doc_scores, feature_names, top_n_features = args

    # 将分数和特征名匹配，形成一个 (score, feature) 列表
    # 只保留分数大于0的词
    feature_scores = [(score, feature_names[i]) for i, score in enumerate(doc_scores) if score > 0]
    
    # 按分数降序排列
    feature_scores.sort(key=lambda x: x[0], reverse=True)
    
    # 提取 Top N 的特征词
    top_features = feature_scores[:top_n_features]
    
    # 1. 格式化输出字符串，用于 Excel 报告 (包含关键词和分数)
    result_list_excel = [f"{tag} ({score:.4f})" for score, tag in top_features]
    
    # 2. 格式化输出字符串，用于文件名后缀 (只包含关键词，不含分数)
    result_list_tags = [tag for score, tag in top_features]
    
    # 返回原始索引和双重结果元组
    return original_idx, ('\n'.join(result_list_excel), result_list_tags)


def calculate_and_extract_tfidf(
    df: pd.DataFrame, 
    corpus: List[str], 
    cleaned_tags_series: pd.Series, 
    top_n_features: int
) -> Tuple[List[str], List[List[str]]]:
    """
    核心功能：计算TF-IDF权重，并使用多进程为每行提取最具区分度的关键词。
    
    Returns: 
        Tuple[List[str], List[List[str]]]:
            1. final_excel_features: 用于 Excel 报告的格式化字符串列表。
            2. final_tag_lists: 用于文件名后缀的关键词列表的列表。
    """
    try:
        logger.info("开始初始化 TfidfVectorizer 并计算 TF-IDF 矩阵...")
    except NameError:
        print("开始初始化 TfidfVectorizer 并计算 TF-IDF 矩阵...")
    
    # 初始化 TfidfVectorizer，确保可以匹配包含下划线的词汇
    vectorizer = TfidfVectorizer(token_pattern=r'(?u)\b\w+\b')
    
    try:
        # 计算 TF-IDF 矩阵
        tfidf_matrix = vectorizer.fit_transform(corpus)
    except Exception as e:
        try:
            logger.error(f"TF-IDF 计算失败: {e}")
        except NameError:
            print(f"TF-IDF 计算失败: {e}")
        # 失败时返回两个空的或带错误信息的列表
        return ["TF-IDF计算失败"] * len(df), [[]] * len(df)

    feature_names = vectorizer.get_feature_names_out()
    try:
        logger.info(f"TF-IDF 计算完成。总词汇量: {len(feature_names)}")
    except NameError:
        print(f"TF-IDF 计算完成。总词汇量: {len(feature_names)}")

    
    # 将稀疏矩阵转换为 NumPy Dense 数组，以便于并行处理时传递数据
    tfidf_array = tfidf_matrix.toarray()
    
    # 1. 创建索引映射：将原始 DataFrame 的索引映射到 tfidf_array 的行索引
    non_empty_indices = cleaned_tags_series[cleaned_tags_series != ''].index.tolist()
    index_map = {original_idx: tfidf_row_idx for tfidf_row_idx, original_idx in enumerate(non_empty_indices)}
    
    # 2. 准备任务列表
    tasks = []
    for original_idx, tfidf_row_idx in index_map.items():
        doc_scores = tfidf_array[tfidf_row_idx]
        # 任务元组: (原始索引, 分数数组, 特征名数组, Top N 数量)
        tasks.append((original_idx, doc_scores, feature_names, top_n_features))

    # --- 计数器/进度跟踪变量初始化 ---
    total_tasks = len(tasks)
    results_map = {} # 存储 {原始索引: (excel_string, tag_list)}
    
    start_time = datetime.datetime.now() # 重新定义 start_time 用于最终计时
    # -----------------------------
    # @@    160-164,166-166   @@ 移除自定义进度跟踪变量，保留 total_tasks 和 results_map

    
    # 3. 执行并行提取
    MAX_WORKERS = os.cpu_count() or 4 # 默认使用 CPU 核心数
    
    try:
        logger.info(f"开始并行提取 Top {top_n_features} 关键词。总任务数: {total_tasks}。使用 {MAX_WORKERS} 个进程...")
    except NameError:
        print(f"开始并行提取 Top {top_n_features} 关键词。总任务数: {total_tasks}。使用 {MAX_WORKERS} 个进程...")

    try:
        with concurrent.futures.ProcessPoolExecutor(max_workers=MAX_WORKERS) as executor:
            # 使用 executor.submit 创建 future 对象，并将其映射回原始索引
            future_to_index = {
                executor.submit(_extract_top_n_for_single_row, task): task[0] 
                for task in tasks
            }
            
            # @@    181-181,185-185   @@ 用 tqdm 包装 as_completed，实现实时进度条
            # 使用 as_completed 可以在任务完成时立即获取结果，实现实时进度跟踪
            for future in tqdm(concurrent.futures.as_completed(future_to_index), total=total_tasks, desc="TF-IDF关键词提取"):
                original_idx = future_to_index[future]
                
                try:
                    # 获取并行任务的结果
                    original_idx_result, result_tuple = future.result()
                    results_map[original_idx_result] = result_tuple
                    
                    # 移除自定义进度打印逻辑 (由 tqdm 代替)
                        
                except Exception as e:
                    # 某个任务失败，记录异常警报
                    error_message = f"【TF-IDF 异常警报】任务失败，原始索引: {original_idx}。错误: {e}"
                    try:
                        logger.error(error_message)
                    except NameError:
                        print(error_message)
                    # 失败的任务在结果集中标记为异常
                    results_map[original_idx] = ("并行处理失败或异常", [])

            # 最终打印完成信息
            final_elapsed_time = (datetime.datetime.now() - start_time).total_seconds()
            
            # 统计成功和失败的任务数（基于结果映射）
            success_count = sum(1 for res in results_map.values() if res[0] not in ["并行处理失败或异常", "无标签数据", "数据异常或为空"])
            failed_count = sum(1 for res in results_map.values() if res[0] == "并行处理失败或异常")
            
            final_log = (
                f"【TF-IDF 计数器总结】总数量: {total_tasks}，"
                f"成功: {success_count}，失败: {failed_count}。"
                f"总耗时: {final_elapsed_time:.2f} 秒。"
            )
            try:
                logger.info(final_log)
            except NameError:
                print(final_log)

    except Exception as e:
        error_message = f"并行处理过程中发生致命错误: {e}"
        try:
            logger.error(error_message)
        except NameError:
            print(error_message)
        return ["并行处理致命失败"] * len(df), [[]] * len(df)


    # 4. 重构最终的结果列表，确保与原始 DataFrame 的行顺序一致
# ... (此函数其余部分内容不变) ...
    final_excel_features = []
    final_tag_lists = []
    
    # 遍历原始 DataFrame 的索引
    for index, _ in df.iterrows():
        if index in results_map:
            # 结果在并行处理的 map 中找到
            excel_string, tag_list = results_map[index]
            final_excel_features.append(excel_string)
            final_tag_lists.append(tag_list)
        else:
            # 结果未找到，说明是空标签（不需要并行处理）
            if cleaned_tags_series.loc[index] == '':
                final_excel_features.append("无标签数据")
                final_tag_lists.append([])
            else:
                 # 理论上不应该发生
                final_excel_features.append("数据异常或为空")
                final_tag_lists.append([])
                
    return final_excel_features, final_tag_lists


def format_tfidf_tags_for_filename(tag_list: List[str], tag_delimiter: str = "___") -> str:
# ... (此函数内容不变) ...
    """
    [新增功能] 将 TF-IDF 关键词列表格式化为文件名后缀字符串。
    
    :param tag_list: 包含 Top N 关键词的列表（如 ['tag1', 'tag2', 'tag3']）。
    :param tag_delimiter: 用于分隔 tag 的字符串，默认为 "___"。
    :return: 格式化的后缀字符串，如 "___tag1___tag2___tag3"。
    """
    if not tag_list:
        return ""
        
    # 确保每个 tag 都是字符串，并去除可能的首尾空格
    cleaned_tags = [tag.strip() for tag in tag_list if tag and isinstance(tag, str)]
    
    if not cleaned_tags:
        return ""

    # 使用 tag_delimiter 连接列表，并在前面添加一个定界符
    # 结果格式: ___tag1___tag2___tag3
    return tag_delimiter + tag_delimiter.join(cleaned_tags)