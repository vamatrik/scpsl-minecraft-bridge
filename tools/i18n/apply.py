"""apply.py FILE FUNC < pairs : каждая пара — две строки: исходный литерал (как в коде) и английский литерал (как в коде), разделитель пустая строка."""
import sys
f, fn = sys.argv[1], sys.argv[2]
s = open(f, encoding='utf-8').read()
blocks = [b for b in sys.stdin.read().split('\n\n') if b.strip()]
miss = 0
for b in blocks:
    ru, en = b.strip('\n').split('\n', 1)
    if ru not in s: print('НЕ НАЙДЕНО:', ru); miss += 1; continue
    s = s.replace(ru, f'{fn}({ru}, {en})')
open(f, 'w', encoding='utf-8').write(s)
print(f, 'готово, пропущено', miss)
