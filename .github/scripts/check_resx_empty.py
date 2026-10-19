# -*- coding: utf-8 -*-
"""resx「鍵存在但值為空」的 CI 閘門 —— 單檔、純標準函式庫、不依賴任何外部工具。

這份是**各 repo `.github/scripts/check_resx_empty.py` 的母本**。
改這裡之後要記得同步到各 repo(它們刻意各存一份,好讓 CI 不依賴本工具箱)。

═══════════════════════════════════════════════════════════════════════════
🔴 為什麼「空值」是崩潰級,而「缺鍵」反而安全
═══════════════════════════════════════════════════════════════════════════
resx 的鍵**存在但 `<value>` 為空** → `ResourceManager.GetString` 回 `""`,
**不會**退回中性資源(ResourceManager 認為該語系有答案,答案就是空字串)。
空字串交給 ImGui → 零長度 span 經 `fixed` 取指標 = null →
`igFindRenderedTextEnd` 把 null 的 text_end 換成 `(char*)-1` → 從位址 0 掃 → C0000005。
**「鍵不存在」才是安全的** —— 那會正常 fallback 到中性(英文)資源。

⇒ 這個閘門存在的理由:上游 crowdin 的 `New translations *.resx` 合併會把地雷帶回來。
   2026-08-19 實測 AutoHook 未合併的上游分支尖端 `5cc8719` 就帶著 4 個空值,
   其中 2 個在 `zh` —— 使用者實際跑的語系,也正是 2026-08-06 把遊戲弄崩的那一種。

⚠️ 這條**只適用 resx**。ECommons 的 `.Loc()` ini 空值是安全的,
   AutoRetainer 自建的 `Loc.T(fallback)` 缺鍵與空值也都安全。不要跨機制套用。

═══════════════════════════════════════════════════════════════════════════
分類與後果
═══════════════════════════════════════════════════════════════════════════
  EMPTY        語系檔(`Foo.zh.resx`)的空值      🔴 崩潰級 → exit 1
                 修法 = **把整個 `<data>` 節點刪掉**,不要補空字串。
  NEUTRAL-EMPTY 中性檔(`Foo.resx`)的空值        🔴 崩潰級 → exit 1
                 ⚠️ **不要直接刪鍵** —— 中性檔的鍵被 `Designer.cs` 引用,刪了編譯失敗。
                 修法 = 補回英文原文。
  WS           值只有空白字元                    ⚠️ 不會崩(長度非 0)→ 只警告,不擋
                 幾乎都是翻譯事故,但空白字串有可能是刻意的版面用途,不硬擋。

  帶 `type=` / `mimetype=` 的 `<data>`(圖片/二進位/型別化資源)一律跳過 ——
  它們的空值語意完全不同。

═══════════════════════════════════════════════════════════════════════════
用法
═══════════════════════════════════════════════════════════════════════════
    python .github/scripts/check_resx_empty.py            # 掃目前工作目錄(CI 用法)
    python .github/scripts/check_resx_empty.py <根目錄>…  # 掃指定目錄
    python .github/scripts/check_resx_empty.py --selftest # 只跑校準閘門

離開碼: 0 = 乾淨(可能有 WS 警告) / 1 = 有空值 / 2 = 校準閘門沒過(結論不可採信)

🔑 **每次掃描前都會自動跑一次合成校準閘門(G1~G7)**。
   沒有它,「掃描器壞掉」與「真的乾淨」在輸出上一模一樣 —— 兩者都印 0。
"""
import io
import os
import sys
import tempfile
import re
import xml.etree.ElementTree as ET

try:
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
except Exception:
    pass

SKIP_DIRS = {'bin', 'obj', '.git', 'node_modules', 'packages', '.vs'}

# Foo.zh.resx / Foo.zh-Hant.resx / Foo.pt-BR.resx -> 語系檔; Foo.resx -> 中性檔
CULTURE_RE = re.compile(r'\.([A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\.resx$')
# 這些「看起來像語系」但其實不是的字尾(resx 檔名沒有標準,只能列黑名單)
NOT_CULTURE = {'designer', 'resources'}


def culture_of(path):
    """回傳語系字串,中性檔回 None。"""
    m = CULTURE_RE.search(os.path.basename(path))
    if not m:
        return None
    tag = m.group(1)
    if tag.lower() in NOT_CULTURE:
        return None
    return tag


def iter_resx(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if fn.endswith('.resx'):
                yield os.path.join(dirpath, fn)


def line_of(raw, name):
    """在原始 bytes 裡找 `name="<name>"` 的行號(1 起算)。找不到回 0。

    ⚠️ 只用來給人看,不參與判斷 —— 判斷一律走 XML 解析結果。
    """
    needle = ('name="%s"' % name).encode('utf-8')
    idx = raw.find(needle)
    if idx < 0:
        return 0
    return raw.count(b'\n', 0, idx) + 1


def scan_file(path):
    """回傳 (字串資源數, `<data>` 節點總數, [(kind, name, line)])。kind in EMPTY / WS。

    🔑 **字串資源數與 `<data>` 節點總數要分開回傳**,因為它們是兩個不同的閘門證據:
       節點總數 = 0 ⇒ 這個檔根本沒解析成功(查詢壞了);
       節點總數 > 0 但字串資源數 = 0 ⇒ 這個 repo 真的只有型別化/二進位資源
       (AutoDuty 的 `Properties/Resources.resx` 就是:2 條全是 `ResXFileRef`)——
       那是**合法的乾淨**,不是掃描器壞掉。混為一談會讓正常的 repo 一直卡在 exit 2。
    """
    with io.open(path, 'rb') as fh:
        raw = fh.read()
    try:
        rootel = ET.fromstring(raw.decode('utf-8-sig'))
    except Exception as e:
        # 壞掉的 XML 要大聲失敗,不能靜默當成「乾淨」
        raise RuntimeError('%s: XML 解析失敗: %s' % (path, e))
    total, nodes, bad = 0, 0, []
    for d in rootel.findall('data'):
        name = d.get('name')
        if name is None:
            continue
        nodes += 1
        if d.get('type') or d.get('mimetype'):
            continue  # 非字串資源
        total += 1
        v = d.find('value')
        text = None if v is None else (v.text or '')
        if text is None or text == '':
            bad.append(('EMPTY', name, line_of(raw, name)))
        elif text.strip() == '':
            bad.append(('WS', name, line_of(raw, name)))
    return total, nodes, bad


# ─────────────────────────────────────────────────────────── 校準閘門
G_POS = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Filled" xml:space="preserve"><value>hello</value></data>
  <data name="EmptyValue" xml:space="preserve"><value></value></data>
  <data name="SelfClosed" xml:space="preserve"><value /></data>
  <data name="NoValueChild" xml:space="preserve"></data>
  <data name="WhitespaceOnly" xml:space="preserve"><value>   </value></data>
  <data name="BinaryEmpty" type="System.Byte[], mscorlib"><value></value></data>
</root>
'''

G_NEG = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="A" xml:space="preserve"><value>alpha</value></data>
  <data name="B" xml:space="preserve"><value>beta</value></data>
</root>
'''

# 只有型別化資源、一條字串資源都沒有(AutoDuty 的 Properties/Resources.resx 就是這個形狀)
G_TYPED = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Ref1" type="System.Resources.ResXFileRef, System.Windows.Forms"><value>a.txt;System.String</value></data>
  <data name="Ref2" type="System.Resources.ResXFileRef, System.Windows.Forms"><value>b.txt;System.String</value></data>
</root>
'''


def selftest(verbose=True):
    """G1 三種空值形狀全中 / G2 純空白值歸 WS 不歸 EMPTY / G3 非字串資源不算 /
    G4 全滿的檔必須回 0(反向對照) / G5 行號真的指到那個鍵 / G6 語系判定兩向 /
    G7 「只有型別化資源」必須是節點數>0、字串數=0、零問題(這是合法的乾淨,不是壞掉)。"""
    ok = True
    d = tempfile.mkdtemp(prefix='resxci')
    p1 = os.path.join(d, 'X.zh.resx')
    p2 = os.path.join(d, 'Y.zh.resx')
    p3 = os.path.join(d, 'Z.resx')
    with io.open(p1, 'wb') as fh:
        fh.write(G_POS.encode('utf-8'))
    with io.open(p2, 'wb') as fh:
        fh.write(G_NEG.encode('utf-8'))
    with io.open(p3, 'wb') as fh:
        fh.write(G_TYPED.encode('utf-8'))

    total, nodes, bad = scan_file(p1)
    kinds = {n: k for k, n, _ in bad}
    if sorted(n for k, n, _ in bad if k == 'EMPTY') != ['EmptyValue', 'NoValueChild', 'SelfClosed']:
        print('G1 FAIL: 三種空值形狀沒有全中: %r' % (bad,)); ok = False
    if kinds.get('WhitespaceOnly') != 'WS':
        print('G2 FAIL: 純空白值沒有被歸成 WS: %r' % (kinds,)); ok = False
    if 'BinaryEmpty' in kinds or total != 5 or nodes != 6:
        print('G3 FAIL: 非字串資源沒有被跳過 (total=%d, nodes=%d, kinds=%r)'
              % (total, nodes, kinds)); ok = False

    t2, n2, b2 = scan_file(p2)
    if b2 or t2 != 2 or n2 != 2:
        print('G4 FAIL: 全滿的檔應該回 0 個問題 (total=%d, nodes=%d, bad=%r)'
              % (t2, n2, b2)); ok = False

    ln = {n: l for k, n, l in bad}
    if ln.get('EmptyValue') != 4:
        print('G5 FAIL: 行號不對, EmptyValue 應在第 4 行, 得到 %r' % ln.get('EmptyValue')); ok = False

    cases = [('UIStrings.zh.resx', 'zh'), ('UIStrings.zh-Hant.resx', 'zh-Hant'),
             ('UIStrings.resx', None), ('Resources.Designer.resx', None),
             ('Language.pt-BR.resx', 'pt-BR')]
    for fn, want in cases:
        got = culture_of(fn)
        if got != want:
            print('G6 FAIL: 語系判定 %s -> %r, 應為 %r' % (fn, got, want)); ok = False

    t3, n3, b3 = scan_file(p3)
    if t3 != 0 or n3 != 2 or b3:
        print('G7 FAIL: 只有型別化資源的檔應該是 字串0/節點2/問題0, 得到 (%d, %d, %r)'
              % (t3, n3, b3)); ok = False

    if verbose:
        print('[resx 空值閘門] 校準閘門 G1~G7: %s' % ('OK' if ok else 'FAIL'))
    return ok


def main():
    argv = sys.argv[1:]
    if '--selftest' in argv:
        return 0 if selftest() else 2

    if not selftest():
        print('🔴 校準閘門沒過 —— 不要相信任何掃描結果', file=sys.stderr)
        return 2

    roots = [a for a in argv if not a.startswith('--')] or [os.getcwd()]

    files_seen = 0
    entries_seen = 0
    nodes_seen = 0
    fatal = []   # EMPTY / NEUTRAL-EMPTY
    warn = []    # WS
    for root in roots:
        root = root.replace('\\', '/').rstrip('/')
        for path in sorted(iter_resx(root)):
            files_seen += 1
            total, nodes, bad = scan_file(path)
            entries_seen += total
            nodes_seen += nodes
            cul = culture_of(path)
            rel = os.path.relpath(path, root).replace('\\', '/')
            for kind, name, line in bad:
                rec = (rel, cul or 'NEUTRAL', kind, name, line)
                if kind == 'EMPTY':
                    fatal.append(rec)
                else:
                    warn.append(rec)

    # 🔑 閘門:一個 resx 都沒掃到 / 一個 <data> 節點都沒解析到 = 查詢壞了,不是「乾淨」
    #    ⚠️ 判「解析成功」要用 **<data> 節點數**,不能用字串資源數 ——
    #       只有型別化資源(ResXFileRef)的 repo 字串數本來就是 0(AutoDuty 就是),
    #       用字串數當閘門會把那種 repo 永遠卡在 exit 2。
    if files_seen == 0:
        print('CALIBRATION FAIL: 一個 .resx 都沒找到(根目錄=%r)—— 路徑錯了或檔案被搬走了,'
              '這不是「乾淨」' % (roots,), file=sys.stderr)
        return 2
    if nodes_seen == 0:
        print('CALIBRATION FAIL: 掃了 %d 個 resx 但一個 <data> 節點都沒解析到' % files_seen,
              file=sys.stderr)
        return 2

    print('[resx 空值閘門] 掃過 %d 個 .resx / %d 個 <data> 節點 / %d 條字串資源'
          % (files_seen, nodes_seen, entries_seen))
    if entries_seen == 0:
        print('[resx 空值閘門] 註:這個 repo 的 resx 全是型別化/二進位資源,沒有字串資源可檢查。')

    if warn:
        print('')
        print('⚠️ 純空白值(不會崩,但幾乎都是翻譯事故;不擋 CI):')
        for rel, cul, _kind, name, line in warn:
            print('   WS   %-10s line %-5d %-45s %s' % (cul, line, name, rel))

    if fatal:
        print('')
        print('🔴 發現 %d 個空值資源 —— 這是崩潰級缺陷,不是風格問題。' % len(fatal))
        print('   resx 的鍵存在但值為空 => ResourceManager 回 "" 而**不會** fallback')
        print('   => ImGui 拿到零長度 span => fixed 取指標為 null => 從位址 0 掃 => C0000005。')
        print('')
        for rel, cul, _kind, name, line in fatal:
            tag = 'EMPTY' if cul != 'NEUTRAL' else 'NEUTRAL-EMPTY'
            print('   %-13s %-10s line %-5d %-45s %s' % (tag, cul, line, name, rel))
        print('')
        print('修法:')
        print('  * 語系檔(Foo.zh.resx 這種):把整個 <data> 節點**刪掉**。')
        print('    「鍵不存在」會正常退回中性資源,是安全的;補空字串則不是。')
        print('  * 中性檔(Foo.resx):**不要刪鍵**(Designer.cs 會引用,刪了編譯失敗)——')
        print('    補回英文原文。')
        return 1

    print('[resx 空值閘門] OK:沒有空值資源。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
