using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 소스 파일이 규약대로 저장되어 있는지 검사합니다.
    ///
    /// <b>왜 테스트가 이것을 맡는가</b>
    ///
    /// <c>.editorconfig</c> 는 편집기에게 <b>부탁</b>할 뿐 강제하지 못합니다. 그 부탁을 듣지 않는
    /// 도구로 파일을 한 번 저장하면 인코딩이 조용히 바뀌고, <b>컴파일은 그대로 통과합니다.</b>
    /// 실제로 그렇게 갈렸습니다 — 2026-08-12 이전에는 <c>.cs</c> 181개 중 40개에만 BOM 이 있었습니다.
    ///
    /// 증상은 한참 뒤에, 전혀 다른 자리에서 나타납니다. BOM 이 없는 파일을 다른 도구가
    /// 시스템 코드페이지(CP949)로 오인해 읽고 다시 저장하는 순간 <b>한글 주석이 통째로 깨집니다.</b>
    /// 이 프로젝트의 주석은 설계 근거의 저장소라 그 손실이 되돌릴 수 없습니다.
    ///
    /// 컴파일러도 런타임도 잡지 못하고 겉으로는 아무 일도 일어나지 않는 종류 —
    /// 기술 문서 §7.1 · §7.4 · §7.5 와 같습니다. <b>테스트만이 유일한 방어선입니다.</b>
    ///
    /// <b>줄바꿈은 검사하지 않습니다</b>
    ///
    /// <c>.gitattributes</c> 가 커밋 시점에 정규화하므로 작업 트리의 줄바꿈은 플랫폼마다 다릅니다.
    /// 리눅스에서 받아 낸 검사 서버는 LF 를 보게 되고, 그것은 결함이 아닙니다.
    /// 여기서 CRLF 를 요구하면 <b>고칠 수 없는 실패</b>가 됩니다.
    /// BOM 을 검사하는 이유가 정확히 그 반대입니다 — <b>git 이 손대 주지 않기 때문</b>입니다.
    /// </summary>
    public sealed class SourceEncodingTests
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// BOM 이 <b>있어야</b> 하는 확장자입니다. <c>.editorconfig</c> 의 <c>utf-8-bom</c> 목록과 같습니다.
        ///
        /// 이 배열이 그 규칙의 <b>유일한 주인</b>입니다. 확장자를 늘릴 때는 두 곳을 함께 고쳐야 하지만,
        /// 어긋나면 이 검사가 곧바로 빨간불을 켜므로 조용히 갈라지지는 않습니다.
        /// </summary>
        private static readonly string[] BomRequiredExtensions =
        {
            ".cs", ".shader", ".hlsl", ".cginc", ".compute",
        };

        /// <summary>
        /// BOM 이 <b>없어야</b> 하는 확장자입니다.
        ///
        /// 반대 방향도 검사하는 이유가 있습니다. CSV 임포터 같은 커스텀 파서는 BOM 을
        /// <b>첫 컬럼의 값</b>으로 읽습니다. 그러면 첫 줄만 매칭에 실패하는데,
        /// 그 증상은 "가끔 한 행이 빠진다"로만 보입니다.
        /// </summary>
        private static readonly string[] BomForbiddenExtensions =
        {
            ".json", ".csv", ".md", ".txt", ".yml", ".yaml",
        };

        /// <summary>UTF-8 BOM 바이트입니다.</summary>
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        /// <summary>
        /// 소스 파일에 BOM 이 있습니다.
        /// </summary>
        [Test]
        public void 소스_파일은_UTF8_BOM_으로_저장된다()
        {
            var offenders = Collect(BomRequiredExtensions, wantBom: false);

            Assert.IsEmpty(
                offenders,
                BuildMessage(
                    $"BOM 이 없는 소스 파일이 {offenders.Count}개 있습니다.",
                    offenders,
                    "고치는 방법은 09.Docs/Tech/00_폴더구조_및_어셈블리.md §6 에 있습니다."));
        }

        /// <summary>
        /// 데이터 파일에는 BOM 이 없습니다.
        /// </summary>
        [Test]
        public void 데이터_파일에는_BOM_이_없다()
        {
            var offenders = Collect(BomForbiddenExtensions, wantBom: true);

            Assert.IsEmpty(
                offenders,
                BuildMessage(
                    $"BOM 이 붙은 데이터 파일이 {offenders.Count}개 있습니다.",
                    offenders,
                    "커스텀 파서가 BOM 을 첫 컬럼 값으로 읽습니다."));
        }

        /// <summary>
        /// 모든 텍스트 파일이 올바른 UTF-8 입니다.
        ///
        /// <b>BOM 검사만으로는 부족합니다.</b> BOM 이 붙어 있어도 본문이 CP949 로 저장돼 있으면
        /// 그 파일은 이미 깨진 것입니다. 엄격 모드로 한 번 해독해 보면 그것이 드러납니다.
        /// </summary>
        [Test]
        public void 모든_텍스트_파일이_올바른_UTF8_이다()
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var offenders = new List<string>();

            foreach (string path in EnumerateFiles(BomRequiredExtensions))
            {
                if (!IsValidUtf8(path, strict))
                {
                    offenders.Add(Relative(path));
                }
            }

            foreach (string path in EnumerateFiles(BomForbiddenExtensions))
            {
                if (!IsValidUtf8(path, strict))
                {
                    offenders.Add(Relative(path));
                }
            }

            Assert.IsEmpty(
                offenders,
                BuildMessage(
                    $"UTF-8 로 해독되지 않는 파일이 {offenders.Count}개 있습니다.",
                    offenders,
                    "CP949 로 저장된 것입니다. 한글이 이미 깨져 있을 수 있으니 내용을 확인하십시오."));
        }

        // ====================================================================================================
        // 3. Helpers
        // ====================================================================================================

        /// <summary>검사 대상 뿌리입니다. 우리 코드만 봅니다 — 외부 에셋은 남의 규약을 따릅니다.</summary>
        private static string Root => Path.Combine(Application.dataPath, "_Project");

        /// <summary>
        /// 규약을 어긴 파일을 모읍니다. <b>처음 하나에서 멈추지 않습니다.</b>
        ///
        /// 하나씩 알려 주면 고치고 다시 돌리기를 어긴 개수만큼 반복하게 됩니다.
        /// <c>DIValidation.RequireRef</c> 가 빈 참조를 한 번에 전부 드러내는 것과 같은 이유입니다.
        /// </summary>
        /// <param name="extensions">검사할 확장자입니다.</param>
        /// <param name="wantBom">BOM 이 <b>있는</b> 것을 위반으로 볼지 여부입니다.</param>
        /// <returns>위반한 파일의 상대 경로 목록입니다. 정렬되어 있습니다.</returns>
        private static List<string> Collect(string[] extensions, bool wantBom)
        {
            var offenders = new List<string>();

            foreach (string path in EnumerateFiles(extensions))
            {
                if (HasBom(path) == wantBom)
                {
                    offenders.Add(Relative(path));
                }
            }

            offenders.Sort(System.StringComparer.Ordinal);
            return offenders;
        }

        /// <summary>지정 확장자의 파일을 전부 훑습니다.</summary>
        /// <param name="extensions">훑을 확장자입니다.</param>
        /// <returns>절대 경로입니다.</returns>
        private static IEnumerable<string> EnumerateFiles(string[] extensions)
        {
            if (!Directory.Exists(Root))
            {
                yield break;
            }

            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(path);

                for (int i = 0; i < extensions.Length; i++)
                {
                    if (string.Equals(extension, extensions[i], System.StringComparison.OrdinalIgnoreCase))
                    {
                        yield return path;
                        break;
                    }
                }
            }
        }

        /// <summary>첫 세 바이트가 BOM 인지 봅니다. 파일 전체를 읽지 않습니다.</summary>
        /// <param name="path">볼 파일입니다.</param>
        /// <returns>BOM 으로 시작하면 true 입니다.</returns>
        private static bool HasBom(string path)
        {
            var head = new byte[3];

            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(head, 0, 3) < 3)
                {
                    return false;
                }
            }

            return head[0] == Bom[0] && head[1] == Bom[1] && head[2] == Bom[2];
        }

        /// <summary>엄격 모드로 해독해 봅니다. 잘못된 바이트가 있으면 예외가 납니다.</summary>
        /// <param name="path">볼 파일입니다.</param>
        /// <param name="strict">잘못된 바이트에서 예외를 내는 인코딩입니다.</param>
        /// <returns>올바른 UTF-8 이면 true 입니다.</returns>
        private static bool IsValidUtf8(string path, UTF8Encoding strict)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                int offset = HasBom(path) ? Bom.Length : 0;

                strict.GetString(bytes, offset, bytes.Length - offset);
                return true;
            }
            catch (System.Text.DecoderFallbackException)
            {
                return false;
            }
        }

        /// <summary>절대 경로를 프로젝트 기준 상대 경로로 줄입니다. 실패 메시지를 읽을 만하게 합니다.</summary>
        /// <param name="path">줄일 절대 경로입니다.</param>
        /// <returns><c>Assets/</c> 부터 시작하는 경로입니다.</returns>
        private static string Relative(string path)
        {
            string normalized = path.Replace('\\', '/');
            int index = normalized.IndexOf("/Assets/", System.StringComparison.Ordinal);

            return index >= 0 ? normalized.Substring(index + 1) : normalized;
        }

        /// <summary>
        /// 위반 목록을 실패 메시지로 만듭니다.
        ///
        /// 목록이 길면 앞의 몇 개만 보입니다. 수십 줄이 쏟아지면 정작 <b>무엇이 문제인지</b>가
        /// 스크롤 위로 밀려 올라가기 때문입니다.
        /// </summary>
        /// <param name="headline">무엇이 잘못됐는지입니다.</param>
        /// <param name="offenders">위반한 파일 목록입니다.</param>
        /// <param name="remedy">어떻게 고치는지입니다.</param>
        /// <returns>조립된 실패 메시지입니다.</returns>
        private static string BuildMessage(string headline, List<string> offenders, string remedy)
        {
            const int MaxListed = 15;

            var builder = new StringBuilder();

            builder.AppendLine(headline);
            builder.AppendLine(remedy);
            builder.AppendLine();

            int listed = Mathf.Min(offenders.Count, MaxListed);

            for (int i = 0; i < listed; i++)
            {
                builder.Append("  ").AppendLine(offenders[i]);
            }

            if (offenders.Count > listed)
            {
                builder.Append("  … 외 ").Append(offenders.Count - listed).AppendLine("개");
            }

            return builder.ToString();
        }
    }
}
