// 文件名：AnalyzerConfig.cs

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; 
using System.Linq; // 引入 Linq 命名空间，方便处理集合
using System.IO;

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
        /// 这些是常见的风格词和质量标签，用于清洗提示词。
        /// 优先从 `Resources/POSITIVE_PROMPT_STOP_WORDS.txt` 加载，文件不存在时回退到内嵌默认列表。
        /// </summary>
        public static readonly ReadOnlyCollection<string> PositivePromptStopWords;

        // 内嵌的回退列表（仅在资源文件缺失时使用）
        private static readonly List<string> _positivePromptStopWordsFallback = new List<string>
        {
            "newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_(been), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, houkisei, sora_72-iro, machi_(machi0910), mochirong, ",
            "newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_(been), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, cutesexyrobutts, ",
            "newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_(been), gweda, fuzichoco, shanguier, anmi, missile228, atdan, iizuki_tasuku, piromizu, binggong_asylum, sheya, dishwasher1910, omone_hokoma_agm, puuzaki_puuna, m-da_s-tarou, ",
            // 艺术家和风格词组（来自 image_scanner.py）
            "newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_(been), gweda, fuzichoco, shanguier, anmi, missile228, atdan, ",
            "newest, 2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_(been), gweda, fuzichoco, shanguier, anmi, missile228, ",
            "newest,2025,toosaka_asagi,novel_illustration,torino_aqua,izumi_tsubasu,oyuwari,pottsness,yunsang,hito_komoru,akeyama_kitsune,fi-san,rourou_(been),gweda,fuzichoco,shanguier,anmi,missile228,",
            "newest,2026,toosaka_asagi,novel_illustration,torino_aqua,izumi_tsubasu,oyuwari,pottsness,yunsang,hito_komoru,akeyama_kitsune,fi-san,rourou_(been),gweda,fuzichoco,shanguier,anmi, ",
            "newest, yunsang, hito_komoru, akeyama_kitsune, fi-san, rourou_(been), gweda, fuzichoco, shanguier, anmi, ",
            "2025, toosaka_asagi, novel_illustration, torino_aqua, izumi_tsubasu, oyuwari, pottsness, ",
            
            // 质量标签和修饰词
            "masterpiece, best quality, amazing quality, very awa, absurdres, newest, very aesthetic, depth of field, ",
            "very awa, absurdres, newest, very aesthetic, depth of field, ",
            "dynamic angle, dutch_angle, tinker bell (pixiv 10956015), masterpiece, best quality, amazing quality, very awa, absurdres, newest, very aesthetic, depth of field, ",
            
            // 简短的风格词组
            "sexy and cute, ",
            "dynamic pose, sexy pose, ",
            
            // 视觉效果词
            "see-through, see-through_clothes, transparent, front_light, frontlight, flat_lighting, soft_light, ",
            "see-through, transparent, ",
        };

        static AnalyzerConfig()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var resourcePath = Path.Combine(baseDir, "Resources", "POSITIVE_PROMPT_STOP_WORDS.txt");
                List<string> lines = null;

                if (File.Exists(resourcePath))
                {
                    // 读取文件，保留非空且非注释行（以 '#' 开头视为注释）
                    var fileLines = File.ReadAllLines(resourcePath);
                    lines = fileLines
                        .Select(l => l?.Trim())
                        .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                        .ToList();
                }

                var finalList = (lines != null && lines.Count > 0) ? lines : _positivePromptStopWordsFallback;
                PositivePromptStopWords = new ReadOnlyCollection<string>(finalList);
            }
            catch
            {
                // 出现任何异常时，回退到内嵌列表以保证程序可运行
                PositivePromptStopWords = new ReadOnlyCollection<string>(_positivePromptStopWordsFallback);
            }
        }
        
        /// <summary>
        /// 自定义风格词库（从扫描数据中动态提取，用于功能9）。
        /// 初始为空，在运行时由功能8填充。
        /// </summary>
        public static Dictionary<string, int> DynamicStyleWordsCache = new Dictionary<string, int>();
        
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