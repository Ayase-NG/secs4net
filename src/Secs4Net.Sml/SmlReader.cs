using CommunityToolkit.HighPerformance;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static Secs4Net.Item;

namespace Secs4Net.Sml;

/// <summary>
/// SML（SECS Message Language）解析器
/// </summary>
/// <remarks>
/// SML是一种人类可读的SECS消息文本格式，用于定义和表示SECS-II消息。
/// 
/// SML格式示例：
/// <code>
/// DVLA:'S2F45' W
///     &lt;L[2]
///         &lt;U1[1] 0>
///         &lt;L[1]
///             &lt;L[2]
///                 &lt;U1[0]>
///                 &lt;L[1]
///                     &lt;L[2]
///                         &lt;B[0]>
///                         &lt;L[2]
///                             &lt;U1[0]>
///                             &lt;U1[0]>
///                         >
///                     >
///                 >
///             >
///         >
///     >
/// .
/// </code>
/// 
/// 格式说明：
/// - 第一行：'[Name]:'S{S}F{F}' [W]'，其中Name为消息别名，W表示需要回复
/// - Item格式：'&lt;Format[Count] Value>'，例如 &lt;U1[1] 0>
/// - '&lt;' 和 '>' 包围Item
/// - '[' 和 ']' 包含Count（元素数量或字节长度）
/// - '.' 表示消息结束
/// - 缩进用于可读性，不影响解析
/// </remarks>
public static class SmlReader
{
    /// <summary>
    /// 将TextReader中的多个SML消息转换为异步SecsMessage流
    /// </summary>
    /// <param name="reader">文本读取器（通常为文件流）</param>
    /// <returns>SecsMessage的异步枚举</returns>
    /// <remarks>
    /// 逐行读取TextReader，每遇到一个以'.'结尾的消息就生成一个SecsMessage。
    /// 常用于从文件中读取多个消息定义。
    /// </remarks>
    public static async IAsyncEnumerable<SecsMessage> ToSecsMessages(this TextReader reader)
    {
        var stack = new Stack<List<Item>>();
        while (reader.Peek() != -1)
        {
            yield return await reader.ToSecsMessageAsync(stack).ConfigureAwait(false);
            stack.Clear();
        }
    }

    /// <summary>
    /// 将SML字符串转换为SecsMessage（同步版本）
    /// </summary>
    /// <param name="str">SML格式的字符串</param>
    /// <returns>解析后的SecsMessage</returns>
    /// <example>
    /// <code>
    /// var sml = "AreYouThere:'S1F1' W";
    /// var message = sml.ToSecsMessage();
    /// </code>
    /// </example>
    public static SecsMessage ToSecsMessage(this string str)
    {
        using var sr = new StringReader(str);
        return sr.ToSecsMessage();
    }

    /// <summary>
    /// 将TextReader中的单个SML消息转换为SecsMessage（异步版本）
    /// </summary>
    /// <param name="sr">文本读取器</param>
    /// <returns>解析后的SecsMessage</returns>
    public static Task<SecsMessage> ToSecsMessageAsync(this TextReader sr)
    {
        return sr.ToSecsMessageAsync(new Stack<List<Item>>());
    }

    /// <summary>
    /// 解析单个SML消息的内部实现（异步版本）
    /// </summary>
    /// <remarks>
    /// 解析流程：
    /// 1. 读取第一行，解析消息头（Name、S、F、ReplyExpected）
    /// 2. 循环读取后续行，解析Item数据
    /// 3. 遇到'.'结束符时返回SecsMessage
    /// 
    /// Item解析采用栈结构处理嵌套List：
    /// - 遇到'&lt;L[...]'：将新List压栈
    /// - 遇到数据Item：添加到当前栈顶的List
    /// - 遇到'>'：弹出栈顶List，转换为Item，添加到新的栈顶List（如果有）
    /// </remarks>
    private static async Task<SecsMessage> ToSecsMessageAsync(this TextReader sr, Stack<List<Item>> stack)
    {
        var line = await sr.ReadLineAsync().ConfigureAwait(false);
#if NET
        var (name, s, f, replyExpected) = ParseFirstLine(line);
#else
        var (name, s, f, replyExpected) = ParseFirstLine(line.AsSpan());
#endif

        Item? rootItem = null;

#if NET
        while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null && ParseItem(line, stack, ref rootItem)) { }
#else
        while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null && ParseItem(line.AsSpan(), stack, ref rootItem)) { }
#endif

        return new SecsMessage(s, f, replyExpected)
        {
            Name = name,
            SecsItem = rootItem,
        };

        /// <summary>
        /// 解析第一行，提取消息别名、S、F和是否需要回复
        /// </summary>
        /// <remarks>
        /// 支持格式：
        /// - "AreYouThere:'S1F1' W"
        /// - "'S1F1' W"
        /// - "'S1F1'"
        /// 
        /// 解析步骤：
        /// 1. 查找':'，':'前为Name
        /// 2. 查找"'S"，提取S编号
        /// 3. 查找'F'，提取F编号
        /// 4. 查找"'"，定位F编号结束
        /// 5. 检查'W'是否存在
        /// </remarks>
        static (string name, byte s, byte f, bool replyExpected) ParseFirstLine(ReadOnlySpan<char> line)
        {
            // 查找':'，':'前为消息别名
            int i = line.IndexOf(':');

            var name = i > 0 ? line[..i].ToString() : string.Empty;
            line = line[name.Length..];
            
            // 查找"'S"找到S编号的起始位置
#if NET
            i = line.IndexOf("'S", StringComparison.Ordinal) + 2;
#else
            i = line.IndexOf("'S".AsSpan(), StringComparison.Ordinal) + 2;
#endif

            // 查找'F'分隔S和F
            int j = line.IndexOf('F');

            // 提取S编号
#if NET
            var s = byte.Parse(line[i..j], provider: CultureInfo.InvariantCulture);
#else
            var s = byte.Parse(line[i..j].ToString(), CultureInfo.InvariantCulture);
#endif

            // F编号在'F'之后到'''之前
            line = line[(j + 1)..];
            i = line.IndexOf('\'');

#if NET
            var f = byte.Parse(line[0..i], provider: CultureInfo.InvariantCulture);
#else
            var f = byte.Parse(line[0..i].ToString(), CultureInfo.InvariantCulture);
#endif

            // 检查是否包含'W'（需要回复）
            var replyExpected = line[i..].IndexOf('W') != -1;
            return (name, s, f, replyExpected);
        }
    }

    /// <summary>
    /// 将TextReader中的单个SML消息转换为SecsMessage（同步版本）
    /// </summary>
    /// <param name="sr">文本读取器</param>
    /// <returns>解析后的SecsMessage</returns>
    public static SecsMessage ToSecsMessage(this TextReader sr)
    {
#if NET
        ReadOnlySpan<char> line = sr.ReadLine();
#else
        ReadOnlySpan<char> line = sr.ReadLine().AsSpan();
#endif
        // 解析第一行，提取消息别名、S、F和是否需要回复
        int i = line.IndexOf(':');

        var name = i > 0 ? line[..i].ToString() : string.Empty;

        line = line[name.Length..];

#if NET
        i = line.IndexOf("'S", StringComparison.Ordinal) + 2;
#else
        i = line.IndexOf("'S".AsSpan(), StringComparison.Ordinal) + 2;
#endif

        int j = line.IndexOf('F');

#if NET
        var s = byte.Parse(line[i..j], provider: CultureInfo.InvariantCulture);
#else
        var s = byte.Parse(line[i..j].ToString(), CultureInfo.InvariantCulture);
#endif

        line = line[(j + 1)..];
        i = line.IndexOf('\'');

#if NET
        var f = byte.Parse(line[0..i], provider: CultureInfo.InvariantCulture);
#else
        var f = byte.Parse(line[0..i].ToString(), CultureInfo.InvariantCulture);
#endif

        var replyExpected = line[i..].IndexOf('W') != -1;

        Item? rootItem = null;
        var stack = new Stack<List<Item>>();

#if NET
        while ((line = sr.ReadLine()) != null && ParseItem(line, stack, ref rootItem)) { }
#else
        while ((line = sr.ReadLine().AsSpan()) != null && ParseItem(line, stack, ref rootItem)) { }
#endif

        return new SecsMessage(s, f, replyExpected)
        {
            Name = name,
            SecsItem = rootItem,
        };
    }

    /// <summary>
    /// 解析单行Item定义
    /// </summary>
    /// <param name="line">SML行内容</param>
    /// <param name="stack">用于跟踪嵌套List的栈</param>
    /// <param name="rootSecsItem">输出参数，根Item</param>
    /// <returns>如果继续解析返回true，遇到'.'结束符返回false</returns>
    /// <remarks>
    /// Item行格式：
    /// - 数据Item：'&lt;Format[Count] Value>'
    /// - List开始：'&lt;L[Count]'
    /// - List结束：'>'（闭合括号）
    /// - 消息结束：'.'（单独一行）
    /// 
    /// 解析逻辑：
    /// 1. 跳过行首空白
    /// 2. 检查是否遇到'.'（消息结束）
    /// 3. 检查是否遇到'>'（List结束，弹栈）
    /// 4. 否则解析数据Item（&lt;Format[Count] Value>）：
    ///    - 提取Format（如U1、ASCII、L等）
    ///    - 提取Count（如[3]）
    ///    - 提取Value（用空格分隔的多个值）
    /// 5. 将解析的Item添加到栈顶List，或设为rootSecsItem
    /// </remarks>
    private static bool ParseItem(ReadOnlySpan<char> line, Stack<List<Item>> stack, ref Item? rootSecsItem)
    {
        // 跳过行首空白字符
        line = line.TrimStart();

        // 检查是否消息结束符 '.'
        if (line.DangerousGetReference() is '.')
        {
            return false;
        }

        // 检查是否List结束符 '>'
        if (line.DangerousGetReference() is '>')
        {
            // 弹栈，创建一个List Item
            var itemList = stack.Pop();
            var item = itemList.Count > 0 ? L(itemList) : L();
            
            // 添加到父List或设为根Item
            if (stack.Count > 0)
            {
                stack.Peek().Add(item);
            }
            else
            {
                rootSecsItem = item;
            }

            return true;
        }

        // 解析数据Item：<Format[Count] Value>
        int indexItemL = line.IndexOf('<') + 1;
        Debug.Assert(indexItemL != 0);

        // 提取Format
        int indexSizeL = line[indexItemL..].IndexOf('[') + indexItemL;
        Debug.Assert(indexSizeL != -1);

        var format = line[indexItemL..indexSizeL].Trim();

        // 提取Count
        int indexSizeR = line[indexSizeL..].IndexOf(']') + indexSizeL;
        Debug.Assert(indexSizeR != -1);

#if NET
        int? size = int.TryParse(line[(indexSizeL + 1)..indexSizeR], out var s) ? s : null;
#else
        int? size = int.TryParse(line[(indexSizeL + 1)..indexSizeR].ToString(), out var s) ? s : null;
#endif

        // 根据Format类型处理
        if (format.DangerousGetReferenceAt(0) == 'L')
        {
            // List类型：压栈创建新List
            stack.Push(new List<Item>(size ?? 0));
        }
        else
        {
            // 数据类型：解析值并创建Item
            int indexItemR = line.LastIndexOf('>');
            Debug.Assert(indexItemR != -1);

            var valueStr = line.Slice(indexSizeR + 1, indexItemR - indexSizeR - 1);
            var item = Create(ParseFormat(format), valueStr, size);
            
            // 添加到父List或设为根Item
            if (stack.Count > 0)
            {
                stack.Peek().Add(item);
            }
            else
            {
                rootSecsItem = item;
            }
        }

        return true;
    }

    /// <summary>
    /// 解析十六进制字节值
    /// </summary>
    /// <remarks>
    /// 支持格式：
    /// - 十进制：0-255
    /// - 十六进制：0x00-0xFF
    /// </remarks>
    private static byte HexByteParser(ReadOnlySpan<char> str)
#if NET
        => str.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? byte.Parse(str[2..], NumberStyles.HexNumber, provider: CultureInfo.InvariantCulture)
        : byte.Parse(str, provider: CultureInfo.InvariantCulture);
#else
        => str.StartsWith("0x".AsSpan(), StringComparison.OrdinalIgnoreCase)
        ? byte.Parse(str[2..].ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : byte.Parse(str.ToString(), CultureInfo.InvariantCulture);
#endif

    // 各类型解析器元组：(空Item构造器, 数组Item构造器, 值解析器)
    private static readonly (Func<Item>, Func<byte[], Item>, SpanParser<byte>) BinaryParser = (B, B, HexByteParser);
#if NET
    private static readonly (Func<Item>, Func<sbyte[], Item>, SpanParser<sbyte>) I1Parser = (I1, I1, static span => sbyte.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<short[], Item>, SpanParser<short>) I2Parser = (I2, I2, static span => short.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<int[], Item>, SpanParser<int>) I4Parser = (I4, I4, static span => int.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<long[], Item>, SpanParser<long>) I8Parser = (I8, I8, static span => long.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<byte[], Item>, SpanParser<byte>) U1Parser = (U1, U1, static span => byte.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<ushort[], Item>, SpanParser<ushort>) U2Parser = (U2, U2, static span => ushort.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<uint[], Item>, SpanParser<uint>) U4Parser = (U4, U4, static span => uint.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<ulong[], Item>, SpanParser<ulong>) U8Parser = (U8, U8, static span => ulong.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<float[], Item>, SpanParser<float>) F4Parser = (F4, F4, static span => float.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<double[], Item>, SpanParser<double>) F8Parser = (F8, F8, static span => double.Parse(span, provider: CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<bool[], Item>, SpanParser<bool>) BoolParser = (Boolean, Boolean, bool.Parse);
#else
    private static readonly (Func<Item>, Func<sbyte[], Item>, SpanParser<sbyte>) I1Parser = (I1, I1, static span => sbyte.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<short[], Item>, SpanParser<short>) I2Parser = (I2, I2, static span => short.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<int[], Item>, SpanParser<int>) I4Parser = (I4, I4, static span => int.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<long[], Item>, SpanParser<long>) I8Parser = (I8, I8, static span => long.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<byte[], Item>, SpanParser<byte>) U1Parser = (U1, U1, static span => byte.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<ushort[], Item>, SpanParser<ushort>) U2Parser = (U2, U2, static span => ushort.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<uint[], Item>, SpanParser<uint>) U4Parser = (U4, U4, static span => uint.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<ulong[], Item>, SpanParser<ulong>) U8Parser = (U8, U8, static span => ulong.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<float[], Item>, SpanParser<float>) F4Parser = (F4, F4, static span => float.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<double[], Item>, SpanParser<double>) F8Parser = (F8, F8, static span => double.Parse(span.ToString(), CultureInfo.InvariantCulture));
    private static readonly (Func<Item>, Func<bool[], Item>, SpanParser<bool>) BoolParser = (Boolean, Boolean, static span => bool.Parse(span.ToString()));
#endif
    private static readonly (Func<Item>, Func<string, Item>) AParser = (A, A);
    private static readonly (Func<Item>, Func<string, Item>) JParser = (J, J);

    // 用于去除字符串首尾的特殊字符（空格、单引号、双引号）
    private static readonly char[] trimElement = [' ', '\'', '"'];

    /// <summary>
    /// 将SML格式字符串转换为SecsFormat枚举
    /// </summary>
    /// <remarks>
    /// 支持的格式映射：
    /// | SML  | SecsFormat |
    /// |------|------------|
    /// | A    | ASCII      |
    /// | J, JIS8 | JIS8    |
    /// | B, Binary | Binary |
    /// | Bool, Boolean | Boolean |
    /// | I1-I8 | 对应整数类型 |
    /// | U1-U8 | 对应无符号类型 |
    /// | F4, F8 | 浮点类型 |
    /// | L    | List       |
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SecsFormat ParseFormat(ReadOnlySpan<char> format)
    {
        return format.ToString() switch
        {
            "A" => SecsFormat.ASCII,
            "JIS8" or "J" => SecsFormat.JIS8,
            "Bool" or "Boolean" => SecsFormat.Boolean,
            "Binary" or "B" => SecsFormat.Binary,
            "I1" => SecsFormat.I1,
            "I2" => SecsFormat.I2,
            "I4" => SecsFormat.I4,
            "I8" => SecsFormat.I8,
            "U1" => SecsFormat.U1,
            "U2" => SecsFormat.U2,
            "U4" => SecsFormat.U4,
            "U8" => SecsFormat.U8,
            "F4" => SecsFormat.F4,
            "F8" => SecsFormat.F8,
            "L" => SecsFormat.List,
            _ => ThrowHelper(format),
        };

#if NET
        [DoesNotReturn]
        static SecsFormat ThrowHelper(ReadOnlySpan<char> format) => throw new SecsException($"Unknown SML format: {format}");
#else
        [DoesNotReturn]
        static SecsFormat ThrowHelper(ReadOnlySpan<char> format) => throw new SecsException($"Unknown SML format: " + format.ToString());
#endif
    }

    /// <summary>
    /// 根据Format和值创建Item
    /// </summary>
    /// <remarks>
    /// 数值类型处理：
    /// - 值以空格分隔（如"1 2 3"）
    /// - 空值返回空Item
    /// - 根据size决定是否创建数组
    /// 
    /// 字符串类型处理：
    /// - 首尾的单引号/双引号会被移除
    /// - 空字符串返回空Item
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Item Create(this SecsFormat format, ReadOnlySpan<char> smlValue, int? size = null)
    {
        return format switch
        {
            SecsFormat.ASCII => ParseStringItem(smlValue, AParser),
            SecsFormat.JIS8 => ParseStringItem(smlValue, JParser),
            SecsFormat.Boolean => ParseArrayItem(smlValue, BoolParser, size),
            SecsFormat.Binary => ParseArrayItem(smlValue, BinaryParser, size),
            SecsFormat.I1 => ParseArrayItem(smlValue, I1Parser, size),
            SecsFormat.I2 => ParseArrayItem(smlValue, I2Parser, size),
            SecsFormat.I4 => ParseArrayItem(smlValue, I4Parser, size),
            SecsFormat.I8 => ParseArrayItem(smlValue, I8Parser, size),
            SecsFormat.U1 => ParseArrayItem(smlValue, U1Parser, size),
            SecsFormat.U2 => ParseArrayItem(smlValue, U2Parser, size),
            SecsFormat.U4 => ParseArrayItem(smlValue, U4Parser, size),
            SecsFormat.U8 => ParseArrayItem(smlValue, U8Parser, size),
            SecsFormat.F4 => ParseArrayItem(smlValue, F4Parser, size),
            SecsFormat.F8 => ParseArrayItem(smlValue, F8Parser, size),
            SecsFormat.List => ThrowHelper("Please use Item.L(...) to create list item."),
            _ => ThrowHelper("Unknown SML format: " + format),
        };

#if NET8_0
        //static Item ParseMemoryItem<T>(ReadOnlySpan<char> str, (Func<Item> emptyCreator, Func<T[], Item> creator) parser, int? size)
        //    where T : unmanaged

        //    , ISpanParsable<T>
        //{
        //    var s = SearchValues.Create(" ");
        //    str.IndexOf()
        //    var range = new Range[09];
        //    str.Split(range, ' ');
        //    var valueStrs = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    return valueStrs.IsEmpty()
        //        ? parser.emptyCreator()
        //        : parser.creator(valueStrs.ToArray(parser.converter, size));
        //}
#endif

        /// <summary>
        /// 解析数组类型Item
        /// </summary>
        static Item ParseArrayItem<T>(ReadOnlySpan<char> str, (Func<Item> emptyCreator, Func<T[], Item> creator, SpanParser<T> converter) parser, int? size) where T : unmanaged
        {
            // 以空格分隔值
            var valueStrs = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return valueStrs.IsEmpty()
                ? parser.emptyCreator()
                : parser.creator(valueStrs.ToArray(parser.converter, size));
        }

        /// <summary>
        /// 解析字符串类型Item
        /// </summary>
        static Item ParseStringItem(ReadOnlySpan<char> str, (Func<Item> emptyCreator, Func<string, Item> creator) parser)
        {
            // 去除首尾的特殊字符（空格、单引号、双引号）
            str = str.TrimStart(trimElement).TrimEnd(trimElement);
            return str.IsEmpty
                ? parser.emptyCreator()
                : parser.creator(str.ToString());
        }

        [DoesNotReturn]
        static Item ThrowHelper(string message) => throw new SecsException(message);
    }

    /// <summary>
    /// 根据Format和SML字符串创建Item（便捷方法）
    /// </summary>
    /// <param name="format">SECS格式</param>
    /// <param name="smlValue">SML格式的值字符串</param>
    /// <returns>创建的Item</returns>
    public static Item Create(this SecsFormat format, string smlValue)
        => Create(format, smlValue.AsSpan());
}
