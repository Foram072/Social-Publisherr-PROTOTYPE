import sys
try:
    import psycopg2
    conn = psycopg2.connect("host=localhost dbname=social_apps_poc user=postgres password=Welcome@123")
    cur = conn.cursor()
    cur.execute('SELECT "Id", "Name" FROM "SocialClients" ORDER BY "Id";')
    print("=== SocialClients ===")
    for c in cur.fetchall():
        print(c)
    
    cur.execute('SELECT "Id", "ClientId", "Platform", "AccountName", "ExternalAccountId", "IsConnected" FROM "SocialAccounts" WHERE "Platform" = \'pinterest\' OR "ClientId" IN (SELECT "Id" FROM "SocialClients" WHERE "Name" IN (\'def\', \'DORAA\', \'xyzz\'));')
    print("\n=== Pinterest and Targeted Workspaces Accounts ===")
    for a in cur.fetchall():
        print(a)

    cur.execute('SELECT "Id", "ClientId", "Provider", "ProviderUserName", "IsActive" FROM "SocialConnections" WHERE "Provider" = \'pinterest\';')
    print("\n=== Pinterest SocialConnections ===")
    for cn in cur.fetchall():
        print(cn)

except Exception as e:
    print("Error:", e)
