// 文件名：AnalyzerConfig.cs

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; 
using System.Linq; // 引入 Linq 命名空间，方便处理集合

namespace ImageAnalyzerCore
{
    /// <summary>
    /// [重构第一步] 核心配置类：用于集中管理所有硬编码的列表、常量和系统路径。
    /// 对应 Python 脚本中分散的硬编码配置，提高可维护性。
    /// 难度系数：1/10 (配置分离)
    /// </summary>
    public static class AnalyzerConfig
    {
        // ----------------------------------------------------
        // I. 基础系统与并发配置 (对应 image_scanner.py 中的 MAX_WORKERS 等)
        // ----------------------------------------------------
        
        /// <summary>
        /// 定义最大并发工作线程数。
        /// 仿照 Python 源码中的 os.cpu_count() or 4。
        /// </summary>
        public static readonly int MaxConcurrentWorkers = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 4;

        /// <summary>
        /// 默认的图片文件扩展名列表（小写）。
        /// </summary>
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };


        // ----------------------------------------------------
        // II. 文件名标记配置 (对应 filename_tagger.py)
        // ----------------------------------------------------
        
        /// <summary>
        /// 用于文件名标记的关键词列表。
        /// 对应 Python 源码中的 TAGGING_KEYWORDS。
        /// </summary>
        public static readonly ReadOnlyCollection<string> TaggingKeywords = new ReadOnlyCollection<string>(new List<string>
        {
            "shorekeeper",
            "noshiro","rio_(blue_archive)","taihou","azur_lane","blue_archive","fgo","pokemon","fate","touhou","idolmaster","love_live","bleach","gundam","umamusume","honkai","hololive","one_piece","final_fantasy","persona","zelda","chainsaw_man","nikke","xenoblade","kantai_collection","genshin_impact","love_live","naruto","overwatch","genderswap","futanari", "skeleton", "green_hair","splatoon","boku_no_hero_academia","midoriya_izuku","ashido_mina","band-aid","covered_nipples","undressing","removing_bra","tail_around_neck","asphyxiation","strangling",
            "pasties",           // 乳贴/胸贴
            "cross_pasties",     // 十字乳贴
            "tape",              // 胶带
            "tape_on_nipples",   // 胶带贴在乳头上
            "open_clothes",      // 敞开的衣服
            "open_jacket",       // 敞开的夹克
            "no_pants"           // 没穿裤子
        });

        /// <summary>
        /// 文件名中用于分隔标签的字符串。
        /// </summary>
        public const string TagDelimiter = "___";


        // ----------------------------------------------------
        // III. 图像扫描/标签清洗配置 (对应 image_scanner.py)
        // ----------------------------------------------------

        /// <summary>
        /// 正向提示词的停用词列表 (用于提取核心词)。
        /// 对应 Python 源码中的 POSITIVE_PROMPT_STOP_WORDS。
        /// </summary>
        public static readonly ReadOnlyCollection<string> PositivePromptStopWords = new ReadOnlyCollection<string>(new List<string>
        {
            // ----------------------------------------------------
            // 核心词汇，一行算一个部分，已被视为整体词组 (C# 中用 Linq 分割)
            // ----------------------------------------------------
            // 注意：Python 源码中的列表非常庞大，此处仅列出片段以保持完整性
            "newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_..." 
            // ⚠️ 在实际应用中，应将 Python 源码中该列表的所有元素完整复制到此列表。
            // 由于源码过长且片段未提供完整内容，此处仅使用片段作为占位符。
        });
        
        // ----------------------------------------------------
        // IV. 文件分类配置 (对应 file_categorizer.py)
        // ----------------------------------------------------

        /// <summary>
        /// 默认禁止自动分类的文件夹名称列表 (完整匹配)。
        /// 对应 Python 源码中的 DEFAULT_PROTECTED_FOLDER_NAMES。
        /// </summary>
        public static readonly ReadOnlyCollection<string> ProtectedFolderNames = new ReadOnlyCollection<string>(new List<string>
        {
            "超级精选",
            "超绝",
            "精选",
            "特殊画风",
        });

        /// <summary>
        /// 模糊匹配关键词列表，如果文件夹名称中包含这些关键词，也视为受保护文件夹。
        /// 对应 Python 源码中的 FUZZY_PROTECTED_KEYWORDS。
        /// </summary>
        public static readonly ReadOnlyCollection<string> FuzzyProtectedKeywords = new ReadOnlyCollection<string>(new List<string>
        {
            "特殊",
            "精选",
            "手动",
        });

        /// <summary>
        /// 最终无法分类的图片将被移动到的目标目录名称。
        /// </summary>
        public const string UnclassifiedFolderName = "未分类_待处理";


        // ----------------------------------------------------
        // V. 报告与评分配置 (对应 getIMGINFOandClassify.py 及 image_scorer_supervised.py)
        // ----------------------------------------------------

        /// <summary>
        /// 报告文件名的前缀格式。
        /// </summary>
        public const string ReportFilenamePrefix = "图片信息报告_";

        /// <summary>
        /// 报告中用于存放核心词汇的列名 (对应 Excel 中的 L 列)。
        /// </summary>
        public const string CoreKeywordColumnName = "提取正向词的核心词";

        /// <summary>
        /// 报告中用于存放预测评分的列名。
        /// 对应 image_scorer_supervised.py 中的 PREDICTED_SCORE_COLUMN。
        /// </summary>
        public const string PredictedScoreColumnName = "个性化推荐预估评分";

        /// <summary>
        /// 自定义评分标记的前缀。
        /// 对应 image_scorer_supervised.py 中的 SCORE_PREFIX。
        /// </summary>
        public const string CustomScorePrefix = "@@@评分";
        
        // 评分映射 (对应 image_scorer_supervised.py 中的 RATING_MAP)
        public static readonly IReadOnlyDictionary<string, double> RatingMap = new Dictionary<string, double>
        {
            { "特殊：98分", 98.0 },
            { "超绝", 95.0 },
            { "特殊画风", 90.0 },
            { "超级精选", 85.0 },
            { "精选", 80.0 }
        };

        /// <summary>
        /// 未被人工标记的图片的默认中性分数。
        /// </summary>
        public const double DefaultNeutralScore = 50.0;
    }
}