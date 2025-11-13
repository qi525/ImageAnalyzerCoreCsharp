import pandas as pd
import numpy as np
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import Ridge # 引入岭回归模型用于权重学习
from loguru import logger
import os
import datetime
import warnings
import re # 引入正则表达式库用于新的评分提取功能
from tqdm import tqdm 

# 忽略 openpyxl 相关的警告，保持日志简洁
warnings.simplefilter(action='ignore', category=UserWarning)

# 定义配置类，用于集中管理所有常量和参数 (难度: 1)
class ScorerConfig:
    """个性化推荐评分系统的配置参数"""
    # 定义您的基准评分映射（基于文件夹名称关键词匹配）
    # 这是模型学习的“正面”信号
    RATING_MAP = {
        "特殊：98分": 98,
        "超绝": 95,
        "特殊画风": 90,
        "超级精选": 85,
        "精选": 80
    }
    # 自定义评分标记的前缀
    SCORE_PREFIX = "@@@评分" 
    # 未被人工标记的图片的默认中性分数
    DEFAULT_NEUTRAL_SCORE = 50.0 
    # 核心词汇列的索引（Excel的L列，即第12列，索引11）
    L_COLUMN_INDEX = 11
    # 最终预测评分的列名
    PREDICTED_SCORE_COLUMN = '个性化推荐预估评分'
    # 训练目标分数的列名 (用户已敲定)
    TARGET_SCORE_COLUMN = '偏好定标分'


class ImageScorer:
    """
    图像个性化推荐评分系统的核心处理类。
    负责数据的读取、特征工程、模型训练和结果保存。 (难度: 3)
    """
    def __init__(self, config: ScorerConfig):
        self.config = config
        self.df: pd.DataFrame = None # 存储处理中的DataFrame
        self.vectorizer: TfidfVectorizer = None # 存储TF-IDF向量化器
        self.A_COLUMN_NAME: str | None = None
        self.TAG_COLUMN_NAME: str | None = None

    def _get_l_column_name(self, df: pd.DataFrame) -> str | None:
        """
        根据列的索引 (L列为11) 尝试找出其对应的真实列名。
        这是获取核心词汇（模型特征）的关键步骤。
        """
        L_INDEX = self.config.L_COLUMN_INDEX
        if L_INDEX >= len(df.columns):
            logger.error(f"Excel 文件列数不足 ({len(df.columns)})，无法找到索引 {L_INDEX} 对应的 L 列（核心词汇列）。")
            return None
        return df.columns[L_INDEX]

    def _extract_score_from_path(self, file_path: str) -> float:
        """
        根据文件路径（A列）匹配并提取用户的基准评分。
        
        提取逻辑优先级：
        1. 文件名末端的自定义标记
        2. 文件夹名称关键词匹配
        3. 默认中性评分 (DEFAULT_NEUTRAL_SCORE)
        """
        # --- 1. 尝试从文件名或路径末端提取自定义评分（最高优先级） ---
        pattern = re.compile(rf'{re.escape(self.config.SCORE_PREFIX)}(\d+)', re.IGNORECASE)
        match = pattern.search(file_path)
        if match:
            try:
                score = int(match.group(1))
                # 限制分数在 0-100 范围内，防止输入异常值
                logger.debug(f"通过自定义标记 '{match.group(0)}' 提取到评分: {score}")
                return float(np.clip(score, 0, 100)) 
            except ValueError:
                logger.warning(f"自定义评分标记 '{match.group(0)}' 中的数字无法转换为整数，跳过。")

        # --- 2. 尝试通过文件夹名称关键词匹配 ---
        for keyword, score in self.config.RATING_MAP.items():
            if keyword in file_path:
                logger.debug(f"通过文件夹关键词 '{keyword}' 提取到评分: {score}")
                return float(score)
                
        # --- 3. 未匹配到任何明确评分，返回中性分 ---
        return self.config.DEFAULT_NEUTRAL_SCORE 

    def _setup_and_vectorize(self, input_df: pd.DataFrame) -> tuple[np.ndarray, np.ndarray, np.ndarray] | None:
        """
        数据准备阶段：接受DataFrame、提取评分、TF-IDF向量化。
        返回 (X_all, Y_all, train_indices)。
        """
        self.df = input_df.copy()
        total_count = len(self.df)
        logger.info(f"待处理的DataFrame总记录数: {total_count}")

        if total_count == 0:
            logger.warning("输入的DataFrame中无数据，跳过处理。")
            return None

        # 确定关键列名
        # 强制检查 DataFrame 是否包含任何列
        if len(self.df.columns) == 0:
             logger.error("DataFrame中未发现任何列。")
             return None
             
        self.A_COLUMN_NAME = self.df.columns[0] # A 列始终作为路径/文件名的来源
        self.TAG_COLUMN_NAME = self._get_l_column_name(self.df)
        
        if self.TAG_COLUMN_NAME is None:
            return None

        logger.info(f"已识别的核心词汇列名（特征 X）：'{self.TAG_COLUMN_NAME}'")
        logger.info(f"已识别的路径/文件名列名（评分提取源）：'{self.A_COLUMN_NAME}'")
        
        # --- 统一数据类型 (优化点 1: 确保字符串类型) ---
        # 强制将用于评分和特征提取的列转换为字符串类型，避免 NaN 或混合类型导致的向量化失败
        self.df[self.A_COLUMN_NAME] = self.df[self.A_COLUMN_NAME].fillna('').astype(str)
        self.df[self.TAG_COLUMN_NAME] = self.df[self.TAG_COLUMN_NAME].fillna('').astype(str)

        # --- 提取目标 (Y) ---
        logger.info(f"提取目标分数 (Y) - {self.config.TARGET_SCORE_COLUMN}...")
        # 应用评分提取逻辑
        self.df[self.config.TARGET_SCORE_COLUMN] = self.df[self.A_COLUMN_NAME].apply(
            self._extract_score_from_path
        )
        Y_all = self.df[self.config.TARGET_SCORE_COLUMN].values
        
        # --- 提取特征 (X) ---
        corpus = self.df[self.TAG_COLUMN_NAME].tolist() # 已经是 str 类型
        
        logger.info("开始进行 TF-IDF 向量化（特征工程）...")
        # token_pattern=r'(?u)\b\w+\b' 确保正确分离标签，stop_words=None 避免移除任何标签
        self.vectorizer = TfidfVectorizer(token_pattern=r'(?u)\b\w+\b', stop_words=None)
        X_all = self.vectorizer.fit_transform(corpus) # 稀疏矩阵
        logger.info(f"TF-IDF 矩阵维度: {X_all.shape} (总样本数 x 总词汇数)")
        
        # 找出明确的高分样本索引用于训练（排除默认中性分 50.0 的样本）
        train_indices = np.where(Y_all != self.config.DEFAULT_NEUTRAL_SCORE)[0]
        
        return X_all, Y_all, train_indices

    def score_dataframe(self, input_df: pd.DataFrame) -> pd.DataFrame | None:
        """
        核心评分逻辑：直接接收一个DataFrame进行处理，并返回包含评分的新DataFrame。
        
        此方法将核心的特征工程和模型预测逻辑与文件I/O解耦，
        允许用户直接传入数据或在更复杂的流程中调用。
        
        Args:
            input_df (pd.DataFrame): 包含路径（A列）和核心词汇（L列）的输入数据。
            
        Returns:
            pd.DataFrame | None: 包含 '偏好定标分' 和 '个性化推荐预估评分' 的新DataFrame，失败返回 None。
        """
        try:
            # 1. 数据准备 (提取特征X, 目标Y, 向量化)
            data_package = self._setup_and_vectorize(input_df)
            if data_package is None:
                return None

            X_all, Y_all, train_indices = data_package

            # 2. 运行模型和预测
            final_scores = self._run_model(X_all, Y_all, train_indices)
            if final_scores is None:
                return None

            # 3. 结果合并与返回
            self.df[self.config.PREDICTED_SCORE_COLUMN] = final_scores
            return self.df
            
        except Exception as e:
            logger.error(f"处理DataFrame时发生错误: {e}")
            return None

    def _run_model(self, X_all: np.ndarray, Y_all: np.ndarray, train_indices: np.ndarray) -> np.ndarray | None:
        """
        模型训练和预测阶段。
        返回所有样本的预测评分。 (优化点 2: 明确训练集日志)
        """
        num_total_samples = len(Y_all)
        num_train_samples = len(train_indices)
        
        if num_train_samples == 0:
            logger.error(f"未找到任何明确的 '{self.config.TARGET_SCORE_COLUMN}' 样本用于训练。请检查文件夹命名或自定义标记。")
            return None
        
        # 仅使用有明确基准分的样本进行训练
        X_train = X_all[train_indices]
        Y_train = Y_all[train_indices]
        
        # 记录训练集占总样本的比例，提高透明度
        train_percentage = (num_train_samples / num_total_samples) * 100
        logger.info(f"模型训练数据量：{num_train_samples} 样本 (占总样本 {num_total_samples} 的 {train_percentage:.2f}%)")
        
        # 训练岭回归模型
        logger.info("开始训练岭回归模型以学习个性化词汇权重...")
        model = Ridge(alpha=1.0) # alpha=1.0 是常用的正则化参数
        model.fit(X_train, Y_train)
        logger.info("模型训练完成。")
        
        # 预测所有图片的评分
        logger.info(f"开始使用学到的权重预测所有 {num_total_samples} 张图片的个性化评分...")
        predicted_scores = model.predict(X_all)
        
        # 将预测评分限制在合理的 [0, 100] 范围内，并四舍五入到整数
        final_scores = np.clip(predicted_scores, 0.0, 100.0).round().astype(int)

        # 打印学习到的高权重词汇，帮助您理解模型的偏好
        feature_names = self.vectorizer.get_feature_names_out()
        weights = pd.Series(model.coef_, index=feature_names)
        top_weights = weights.sort_values(ascending=False).head(10)
        logger.info(f"学到的 Top 10 正向权重词汇（影响评分提升，即您偏好的特征）:\n{top_weights.to_string()}")
        
        return final_scores

    def run_scoring_from_file(self, file_path: str) -> None:
        """
        文件评分工作流：负责文件I/O，调用 score_dataframe 进行核心处理，并保存结果。
        [2025-10-31] 更新: 遵循用户要求，直接在原文件上修改，不再创建副本。
        """
        total_count = 0
        success_count = 0
        
        try:
            # --- 1. 读取文件 ---
            logger.info(f"开始读取并直接修改 Excel 文件: {file_path}")
            initial_df = pd.read_excel(file_path)

            # --- 2. 调用核心处理方法 ---
            total_count = len(initial_df)
            
            scored_df = self.score_dataframe(initial_df)
            
            if scored_df is None:
                logger.error("核心评分处理失败，未生成评分结果。")
                return 

            # --- 3. 结果保存 ---
            # 确保使用 score_dataframe 返回的 DataFrame
            self.df = scored_df
            
            logger.info(f"开始写入数据回 Excel 文件: {file_path}")
            # 写入时保留所有原始列和新的评分列
            scored_df.to_excel(file_path, index=False, header=True, engine='openpyxl')
            logger.success(f"成功添加列 '{self.config.TARGET_SCORE_COLUMN}' 和 '{self.config.PREDICTED_SCORE_COLUMN}' 并保存到原文件: {file_path}")
            success_count = total_count

        except FileNotFoundError:
            logger.error(f"文件未找到: {file_path}")
        except Exception as e:
            logger.error(f"处理 Excel 文件时发生错误: {e}")
            logger.critical(f"异常警报：评分计算失败，请检查内存、磁盘空间或数据格式。错误信息: {e}")
        finally:
            # 统计任务量，提供操作反馈
            failure_count = total_count - success_count
            logger.info(f"总任务量: {total_count}, 成功处理量: {success_count}, 失败处理量: {failure_count}")

# --- 主执行逻辑 ---
def main():
    """
    主程序入口：初始化配置、评分器，并执行评分流程。
    """
    # 初始化 Loguru 日志系统
    log_file = "image_scorer_personalized_log.txt"
    logger.remove()
    logger.add(log_file, rotation="10 MB", retention="10 days")
    logger.info("个性化推荐评分系统初始化。")
    
    # 实例化配置和核心评分器
    config = ScorerConfig()
    scorer = ImageScorer(config)

    # 定义您的文件路径（请根据实际情况修改此路径）
    excel_path = r"C:\个人数据\pythonCode\getIMGINFO\图片信息报告_20251027193706.xlsx" # 默认使用文件路径运行
    
    # 检查文件路径，然后执行评分计算
    if os.path.exists(excel_path):
        scorer.run_scoring_from_file(excel_path)
        
        # 自动运行打开日志文件，方便您检查结果和模型学到的权重
        try:
            # os.startfile(log_file) # 注释掉自动打开，避免频繁弹出
            print(f"请在日志中查找操作结果。日志文件: {log_file}")
        except Exception as e:
            logger.warning(f"无法自动打开日志文件。请手动检查: {log_file}。错误: {e}")
    else:
        logger.error(f"文件路径不存在，请检查: {excel_path}")
        print(f"文件路径不存在，请检查: {excel_path}")

if __name__ == '__main__':
    main()