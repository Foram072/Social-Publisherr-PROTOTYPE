import psycopg2
import urllib.request
import json

conn = psycopg2.connect('host=localhost dbname=social_apps_poc user=postgres password=Welcome@123')
cur = conn.cursor()
cur.execute('SELECT "Id", "Provider", "AccessToken" FROM "SocialConnections" WHERE "Provider" IN (\'facebook\', \'instagram\') ORDER BY "Id" DESC LIMIT 5')
rows = cur.fetchall()
for r in rows:
    conn_id, provider, token = r
    print(f"Connection {conn_id} ({provider}): {token[:20]}...")
    url = f"https://graph.facebook.com/v20.0/me/adaccounts?fields=id,name,account_id,account_status,currency,amount_spent&access_token={token}"
    try:
        req = urllib.request.Request(url)
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            print("  Ad Accounts found:", len(data.get("data", [])))
            for acc in data.get("data", []):
                print("   -", acc)
    except urllib.error.HTTPError as e:
        print("  Error:", e.code, e.read().decode('utf-8'))
    except Exception as ex:
        print("  Error:", ex)
