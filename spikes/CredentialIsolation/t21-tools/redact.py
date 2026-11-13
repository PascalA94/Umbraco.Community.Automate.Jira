import sys
sen=open(sys.argv[1]).read().strip()
for l in sys.stdin: sys.stdout.write(l.replace(sen,"<SENTINEL>"))
