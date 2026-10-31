using System.Collections.Generic;
using System.Text;

namespace TekitouKeyborad.Services;

internal static class RomajiInputService
{
    private static readonly Dictionary<string, string> Kana = new()
    {
        ["a"] = "あ", ["i"] = "い", ["u"] = "う", ["e"] = "え", ["o"] = "お",
        ["ka"] = "か", ["ki"] = "き", ["ku"] = "く", ["ke"] = "け", ["ko"] = "こ",
        ["ga"] = "が", ["gi"] = "ぎ", ["gu"] = "ぐ", ["ge"] = "げ", ["go"] = "ご",
        ["sa"] = "さ", ["shi"] = "し", ["si"] = "し", ["su"] = "す", ["se"] = "せ", ["so"] = "そ",
        ["za"] = "ざ", ["ji"] = "じ", ["zi"] = "じ", ["zu"] = "ず", ["ze"] = "ぜ", ["zo"] = "ぞ",
        ["ta"] = "た", ["chi"] = "ち", ["ti"] = "ち", ["tsu"] = "つ", ["tu"] = "つ", ["te"] = "て", ["to"] = "と",
        ["da"] = "だ", ["di"] = "ぢ", ["du"] = "づ", ["de"] = "で", ["do"] = "ど",
        ["na"] = "な", ["ni"] = "に", ["nu"] = "ぬ", ["ne"] = "ね", ["no"] = "の",
        ["ha"] = "は", ["hi"] = "ひ", ["fu"] = "ふ", ["hu"] = "ふ", ["he"] = "へ", ["ho"] = "ほ",
        ["ba"] = "ば", ["bi"] = "び", ["bu"] = "ぶ", ["be"] = "べ", ["bo"] = "ぼ",
        ["pa"] = "ぱ", ["pi"] = "ぴ", ["pu"] = "ぷ", ["pe"] = "ぺ", ["po"] = "ぽ",
        ["ma"] = "ま", ["mi"] = "み", ["mu"] = "む", ["me"] = "め", ["mo"] = "も",
        ["ya"] = "や", ["yu"] = "ゆ", ["yo"] = "よ",
        ["ra"] = "ら", ["ri"] = "り", ["ru"] = "る", ["re"] = "れ", ["ro"] = "ろ",
        ["wa"] = "わ", ["wi"] = "うぃ", ["we"] = "うぇ", ["wo"] = "を",
        ["kya"] = "きゃ", ["kyu"] = "きゅ", ["kyo"] = "きょ",
        ["gya"] = "ぎゃ", ["gyu"] = "ぎゅ", ["gyo"] = "ぎょ",
        ["sha"] = "しゃ", ["shu"] = "しゅ", ["sho"] = "しょ",
        ["sya"] = "しゃ", ["syu"] = "しゅ", ["syo"] = "しょ",
        ["ja"] = "じゃ", ["ju"] = "じゅ", ["jo"] = "じょ",
        ["jya"] = "じゃ", ["jyu"] = "じゅ", ["jyo"] = "じょ",
        ["zya"] = "じゃ", ["zyu"] = "じゅ", ["zyo"] = "じょ",
        ["cha"] = "ちゃ", ["chu"] = "ちゅ", ["cho"] = "ちょ",
        ["tya"] = "ちゃ", ["tyu"] = "ちゅ", ["tyo"] = "ちょ",
        ["nya"] = "にゃ", ["nyu"] = "にゅ", ["nyo"] = "にょ",
        ["hya"] = "ひゃ", ["hyu"] = "ひゅ", ["hyo"] = "ひょ",
        ["bya"] = "びゃ", ["byu"] = "びゅ", ["byo"] = "びょ",
        ["pya"] = "ぴゃ", ["pyu"] = "ぴゅ", ["pyo"] = "ぴょ",
        ["mya"] = "みゃ", ["myu"] = "みゅ", ["myo"] = "みょ",
        ["rya"] = "りゃ", ["ryu"] = "りゅ", ["ryo"] = "りょ",
        ["fa"] = "ふぁ", ["fi"] = "ふぃ", ["fe"] = "ふぇ", ["fo"] = "ふぉ",
        ["va"] = "ゔぁ", ["vi"] = "ゔぃ", ["vu"] = "ゔ", ["ve"] = "ゔぇ", ["vo"] = "ゔぉ"
    };

    internal static string PrepareForProofreading(string input)
    {
        // 全文を安全にかなへ変換できる場合だけ前処理し、固有名詞・混在文などは原文をAIへ渡す。
        foreach (char character in input)
        {
            if (!IsLetter(character) && character is not (' ' or '\t' or '\r' or '\n' or '\'' or '.' or ',' or '!' or '?' or '-'))
            {
                return input;
            }
        }
        for (int i = 1; i + 1 < input.Length; i++)
        {
            if (input[i] == '.' && IsLetter(input[i - 1]) && IsLetter(input[i + 1]))
            {
                return input;
            }
        }

        var result = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length;)
        {
            char current = input[i];
            char next = i + 1 < input.Length ? input[i + 1] : '\0';
            if (!IsLetter(current))
            {
                result.Append(current);
                i++;
                continue;
            }
            if (current == 'n')
            {
                if (next == '\'')
                {
                    result.Append('ん');
                    i += 2;
                    continue;
                }
                if (next == 'n')
                {
                    result.Append('ん');
                    i += i + 2 < input.Length && (IsVowel(input[i + 2]) || input[i + 2] == 'y') ? 1 : 2;
                    continue;
                }
                if (!IsVowel(next) && next != 'y')
                {
                    result.Append('ん');
                    i++;
                    continue;
                }
            }
            if (!IsVowel(current) &&
                (current == next || current == 't' && next == 'c' && i + 2 < input.Length && input[i + 2] == 'h'))
            {
                result.Append('っ');
                i++;
                continue;
            }

            bool converted = false;
            for (int length = System.Math.Min(3, input.Length - i); length >= 1; length--)
            {
                if (Kana.TryGetValue(input.Substring(i, length), out string? kana))
                {
                    result.Append(kana);
                    i += length;
                    converted = true;
                    break;
                }
            }
            if (!converted)
            {
                return input;
            }
        }
        return result.ToString();
    }

    private static bool IsLetter(char character) => character is >= 'a' and <= 'z';
    private static bool IsVowel(char character) => character is 'a' or 'i' or 'u' or 'e' or 'o';
}
