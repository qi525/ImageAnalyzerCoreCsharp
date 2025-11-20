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
        // I. 基础系统与并发配置
        
        public static readonly int MaxConcurrentWorkers = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 4;
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

        // II. 文件名标记配置
        
        public static readonly ReadOnlyCollection<string> TaggingKeywords = new ReadOnlyCollection<string>(new List<string>
        {
            "shorekeeper", "noshiro","rio_(blue_archive)","taihou","azur_lane","blue_archive","fgo","pokemon","fate","touhou","idolmaster","love_live","bleach","gundam","umamusume","honkai","hololive","one_piece","final_fantasy","persona","zelda","chainsaw_man","nikke","xenoblade","kantai_collection","genshin_impact","love_live","naruto","overwatch","genderswap","futanari", "skeleton", "green_hair","splatoon","boku_no_hero_academia","midoriya_izuku","ashido_mina","band-aid","covered_nipples","undressing","removing_bra","tail_around_neck","asphyxiation","strangling",
            "pasties", "cross_pasties", "tape", "tape_on_nipples", "open_clothes", "open_jacket", "no_pants"
        });

        public const string TagDelimiter = "___";

        // III. 图像扫描/标签清洗配置

        public static readonly ReadOnlyCollection<string> PositivePromptStopWords;

        private static readonly List<string> _positivePromptStopWordsFallback = new List<string>();

        static AnalyzerConfig()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                var resourcePath = Path.Combine(baseDir, "Resources", "STYLE_WORDS_FALLBACK.txt");
                List<string>? lines = null;

                if (File.Exists(resourcePath))
                {
                    var fileLines = File.ReadAllLines(resourcePath);
                    lines = fileLines
                        .Select(l => l?.Trim() ?? string.Empty)
                        .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                        .ToList();
                }

                var finalList = (lines != null && lines.Count > 0) ? lines : _positivePromptStopWordsFallback;
                PositivePromptStopWords = new ReadOnlyCollection<string>(finalList);
            }
            catch
            {
                PositivePromptStopWords = new ReadOnlyCollection<string>(_positivePromptStopWordsFallback);
            }
        }
        
        public static Dictionary<string, int> DynamicStyleWordsCache = new Dictionary<string, int>();
        
        // IV. 文件分类配置

        public static readonly ReadOnlyCollection<string> ProtectedFolderNames = new ReadOnlyCollection<string>(new List<string>
        {
            "超级精选", "超绝", "精选", "特殊画风",
        });

        public static readonly ReadOnlyCollection<string> FuzzyProtectedKeywords = new ReadOnlyCollection<string>(new List<string>
        {
            "特殊", "精选", "手动",
        });

        public const string UnclassifiedFolderName = "未分类_待处理";

        // V. 报告与评分配置

        public const string ReportFilenamePrefix = "图片信息报告_";
        public const string CoreKeywordColumnName = "提取正向词的核心词";
        public const string PredictedScoreColumnName = "个性化推荐预估评分";
        public const string CustomScorePrefix = "@@@评分";
        
        public static readonly IReadOnlyDictionary<string, double> RatingMap = new Dictionary<string, double>
        {
            { "特殊：98分", 98.0 },
            { "超绝", 95.0 },
            { "特殊画风", 90.0 },
            { "超级精选", 85.0 },
            { "精选", 80.0 }
        };

        public const double DefaultNeutralScore = 50.0;
    }
}