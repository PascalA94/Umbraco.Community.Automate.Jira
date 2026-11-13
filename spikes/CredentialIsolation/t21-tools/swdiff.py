import json,sys
def load(p):
    d=json.load(open(p)); out={}
    for path,ops in d['paths'].items():
        for m,op in ops.items():
            if m in('get','post','put','delete','patch'):
                params=sorted(f"{p.get('in')}:{p.get('name')}" for p in op.get('parameters',[]))
                body=json.dumps(op.get('requestBody',{}).get('content',{}).get(next(iter(op.get('requestBody',{}).get('content',{}) or {'':0}),''),{}).get('schema',{}),sort_keys=True) if op.get('requestBody') else ''
                resp=sorted(op.get('responses',{}).keys())
                out[(m.upper(),path)]=(params,body,resp)
    return out
a,b=load(sys.argv[1]),load(sys.argv[2])
added=sorted(set(b)-set(a)); removed=sorted(set(a)-set(b))
changed=sorted(k for k in set(a)&set(b) if a[k]!=b[k])
print(f"old {len(a)} ops, new {len(b)} ops; added {len(added)}, removed {len(removed)}, changed {len(changed)}")
for k in added: print("  ADDED  ",*k, b[k][0])
for k in removed: print("  REMOVED",*k)
for k in changed: print("  CHANGED",*k, "params" if a[k][0]!=b[k][0] else "", "body" if a[k][1]!=b[k][1] else "", "responses" if a[k][2]!=b[k][2] else "")
