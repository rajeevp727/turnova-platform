import { StatusBar } from "expo-status-bar";
import * as SecureStore from "expo-secure-store";
import { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";

const API = process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://10.0.2.2:5000";
const ZONE = "hyderabad-central";
const SERVICE = "auto";
type Tokens = { accessToken: string; refreshToken: string };
type User = { id: string; email: string; name?: string; role: string };
type QueueEntry = { driverId: string; position: number; joinedAt: string };

async function saveTokens(tokens: Tokens) {
  await SecureStore.setItemAsync("turnova.access", tokens.accessToken);
  await SecureStore.setItemAsync("turnova.refresh", tokens.refreshToken);
}
async function clearTokens() {
  await SecureStore.deleteItemAsync("turnova.access");
  await SecureStore.deleteItemAsync("turnova.refresh");
}
async function jsonRequest(path: string, init: RequestInit = {}, token?: string) {
  const response = await fetch(`${API}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers }
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.message ?? body.title ?? "Request failed.");
  return body;
}

export default function App() {
  const [user, setUser] = useState<User | null>(null);
  const [tokens, setTokens] = useState<Tokens | null>(null);
  const [tab, setTab] = useState<"Home" | "Queue" | "Trips" | "Profile">("Home");
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [queue, setQueue] = useState<QueueEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  const acceptSession = useCallback(async (result: any) => {
    const next = { accessToken: result.accessToken, refreshToken: result.refreshToken };
    await saveTokens(next);
    setTokens(next);
    setUser(result.user);
    setError("");
  }, []);

  const refreshSession = useCallback(async (refreshToken: string) => {
    const result = await jsonRequest("/api/v1/auth/refresh", {
      method: "POST", body: JSON.stringify({ refreshToken })
    });
    await acceptSession(result);
    return result.accessToken as string;
  }, [acceptSession]);

  const apiFetch = useCallback(async (path: string, init: RequestInit = {}) => {
    if (!tokens) throw new Error("Please sign in again.");
    let response = await fetch(`${API}${path}`, {
      ...init,
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${tokens.accessToken}`, ...init.headers }
    });
    if (response.status === 401) {
      const refreshToken = await SecureStore.getItemAsync("turnova.refresh");
      if (!refreshToken) { await clearTokens(); setTokens(null); setUser(null); throw new Error("Session expired. Please sign in again."); }
      const accessToken = await refreshSession(refreshToken);
      response = await fetch(`${API}${path}`, {
        ...init,
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}`, ...init.headers }
      });
    }
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(body.message ?? body.title ?? "Request failed.");
    return body;
  }, [tokens, refreshSession]);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const accessToken = await SecureStore.getItemAsync("turnova.access");
        const refreshToken = await SecureStore.getItemAsync("turnova.refresh");
        if (accessToken && refreshToken) {
          try {
            const me = await jsonRequest("/api/v1/auth/me", {}, accessToken);
            if (active) { setTokens({ accessToken, refreshToken }); setUser(me); }
          } catch {
            try {
              const result = await jsonRequest("/api/v1/auth/refresh", { method: "POST", body: JSON.stringify({ refreshToken }) });
              if (active) await acceptSession(result);
            } catch { await clearTokens(); }
          }
        }
      } catch (e) { if (active) setError("Unable to restore your session."); }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, [acceptSession]);

  const authenticate = async (replaceOldest = false) => {
    setLoading(true); setError(""); setNotice("");
    try {
      const path = mode === "register" ? "/api/v1/auth/driver/register" : "/api/v1/auth/driver/login";
      const result = await jsonRequest(path, {
        method: "POST",
        body: JSON.stringify({ email: email.trim(), password, name: name.trim() || null, replaceOldest })
      });
      await acceptSession(result);
    } catch (e) { setError(e instanceof Error ? e.message : "Authentication failed."); }
    finally { setLoading(false); }
  };

  const loadQueue = useCallback(async () => {
    try {
      const data = await apiFetch(`/api/v1/zones/${ZONE}/queues/${SERVICE}`);
      setQueue(data);
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to load queue."); }
  }, [apiFetch]);
  useEffect(() => { if (user && tab === "Queue") void loadQueue(); }, [user, tab, loadQueue]);

  const joinQueue = async () => {
    if (!user) return;
    setLoading(true); setError("");
    try {
      const result = await apiFetch("/api/v1/queues/join", {
        method: "POST",
        body: JSON.stringify({ driverId: user.id, zoneId: ZONE, serviceClass: SERVICE })
      });
      setNotice(result.message ?? "You joined the queue.");
      await loadQueue();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to join queue."); }
    finally { setLoading(false); }
  };

  const logout = async () => {
    try { if (tokens) await jsonRequest("/api/v1/auth/logout", { method: "POST" }, tokens.accessToken); }
    catch { /* Local credentials are cleared even if the network is unavailable. */ }
    await clearTokens(); setTokens(null); setUser(null); setQueue([]);
  };

  if (loading && !user) return <SafeAreaView style={styles.safe}><ActivityIndicator color="#E89A3B" style={{ marginTop: 80 }} /></SafeAreaView>;
  if (!user) return (
    <SafeAreaView style={styles.safe}><StatusBar style="light" />
      <View style={styles.header}><Text style={styles.brand}>TurnOva Driver</Text><Text style={styles.tagline}>Everyone’s Turn Matters.</Text></View>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.eyebrow}>DRIVER PARTNER</Text><Text style={styles.title}>{mode === "login" ? "Welcome back" : "Create your account"}</Text>
        <Text style={styles.sub}>Sign in securely with your email and password.</Text>
        <View style={styles.card}>
          {mode === "register" && <TextInput value={name} onChangeText={setName} style={styles.input} placeholder="Full name" autoCapitalize="words" />}
          <TextInput value={email} onChangeText={setEmail} style={styles.input} placeholder="Email address" keyboardType="email-address" autoCapitalize="none" autoCorrect={false} />
          <TextInput value={password} onChangeText={setPassword} style={styles.input} placeholder="Password" secureTextEntry />
          <Pressable style={styles.primary} disabled={loading || !email.trim() || !password} onPress={() => void authenticate()}><Text style={styles.primaryText}>{loading ? "Please wait…" : mode === "login" ? "Sign in" : "Create driver account"}</Text></Pressable>
          <Pressable onPress={() => { setMode(mode === "login" ? "register" : "login"); setError(""); }}><Text style={styles.link}>{mode === "login" ? "New to TurnOva? Create an account" : "Already registered? Sign in"}</Text></Pressable>
          {!!error && <Text style={styles.error}>{error}</Text>}
        </View>
      </ScrollView>
    </SafeAreaView>
  );

  const position = queue.find(x => x.driverId === user.id)?.position;
  const tabs = ["Home", "Queue", "Trips", "Profile"] as const;
  return <SafeAreaView style={styles.safe}><StatusBar style="light" />
    <View style={styles.header}><View><Text style={styles.brand}>TurnOva</Text><Text style={styles.tagline}>Driver partner · {user.name || user.email}</Text></View><Text style={styles.role}>DRIVER</Text></View>
    <ScrollView contentContainerStyle={styles.content}>
      {tab === "Home" && <><Text style={styles.eyebrow}>YOUR DRIVER SPACE</Text><Text style={styles.title}>Welcome, {user.name || "Driver"}</Text><Text style={styles.sub}>Your next opportunity starts with a fair turn.</Text><View style={styles.card}><Text style={styles.label}>SERVICE ZONE</Text><Text style={styles.cardTitle}>Hyderabad Central · Auto</Text><Text style={styles.label}>YOUR QUEUE POSITION</Text><Text style={styles.position}>{position ? `#${position}` : "—"}</Text><Pressable style={styles.primary} onPress={() => setTab("Queue")}><Text style={styles.primaryText}>View queue</Text></Pressable></View></>}
      {tab === "Queue" && <><Text style={styles.eyebrow}>HYDERABAD CENTRAL · AUTO</Text><Text style={styles.title}>Your queue</Text><View style={styles.card}><Text style={styles.label}>CURRENT POSITION</Text><Text style={styles.position}>{position ? `#${position}` : "Not joined"}</Text><Pressable style={styles.primary} onPress={() => void joinQueue()} disabled={loading}><Text style={styles.primaryText}>{loading ? "Please wait…" : position ? "Refresh queue" : "Join FIFO queue"}</Text></Pressable><Pressable onPress={() => void loadQueue()}><Text style={styles.link}>Refresh list</Text></Pressable></View>{queue.map(item => <View style={styles.queueItem} key={item.driverId}><Text style={styles.cardTitle}>#{item.position} · {item.driverId}</Text><Text style={styles.muted}>{new Date(item.joinedAt).toLocaleString()}</Text></View>)}</>}
      {tab === "Trips" && <><Text style={styles.title}>Trips</Text><View style={styles.card}><Text style={styles.muted}>Your accepted offers and trip history will appear here.</Text></View></>}
      {tab === "Profile" && <><Text style={styles.title}>Your profile</Text><View style={styles.card}><Text style={styles.cardTitle}>{user.name || "Driver"}</Text><Text style={styles.muted}>{user.email}</Text><Text style={styles.muted}>Account role: {user.role}</Text><Pressable style={styles.secondary} onPress={() => void logout()}><Text style={styles.secondaryText}>Sign out</Text></Pressable></View></>}
      {!!error && <Text style={styles.error}>{error}</Text>}{!!notice && <Text style={styles.success}>{notice}</Text>}
    </ScrollView>
    <View style={styles.tabBar}>{tabs.map(item => <Pressable key={item} style={styles.tab} onPress={() => setTab(item)}><Text style={[styles.tabText, tab === item && styles.tabActive]}>{item}</Text></Pressable>)}</View>
  </SafeAreaView>;
}

const styles = StyleSheet.create({
  safe:{flex:1,backgroundColor:"#F4F6F8"},header:{backgroundColor:"#14283F",padding:22,paddingTop:18},brand:{color:"#FFF",fontSize:25,fontWeight:"800"},tagline:{color:"#B8C5D2",marginTop:4,fontSize:12},role:{color:"#F7B65F",fontWeight:"800",marginTop:6},content:{padding:22,paddingBottom:34},eyebrow:{color:"#D58B32",fontWeight:"800",fontSize:11,letterSpacing:1.3,marginTop:8},title:{color:"#172B42",fontSize:28,fontWeight:"800",marginTop:8},sub:{color:"#718094",fontSize:14,marginTop:6,marginBottom:20},card:{backgroundColor:"#FFF",padding:20,borderRadius:18,marginBottom:15},input:{borderWidth:1,borderColor:"#E2E8EE",borderRadius:12,padding:14,color:"#20354B",marginBottom:12},primary:{backgroundColor:"#E89A3B",borderRadius:12,padding:15,alignItems:"center",marginTop:12},primaryText:{color:"#172B42",fontWeight:"800"},link:{color:"#B66D1D",fontWeight:"800",textAlign:"center",marginTop:18},label:{color:"#8290A0",fontSize:10,fontWeight:"800",letterSpacing:1.1,marginTop:10},cardTitle:{color:"#172B42",fontSize:18,fontWeight:"800",marginVertical:8},position:{color:"#D98B2D",fontSize:42,fontWeight:"900",marginTop:6},muted:{color:"#758397",fontSize:13,marginTop:5},queueItem:{backgroundColor:"#FFF",padding:14,borderRadius:12,marginBottom:8},error:{color:"#B42318",marginTop:12},success:{color:"#237A54",marginTop:12},secondary:{borderWidth:1,borderColor:"#D7DEE6",borderRadius:12,padding:13,alignItems:"center",marginTop:20},secondaryText:{color:"#30445A",fontWeight:"800"},tabBar:{flexDirection:"row",backgroundColor:"#FFF",paddingVertical:14},tab:{flex:1,alignItems:"center"},tabText:{color:"#8793A2",fontSize:12,fontWeight:"700"},tabActive:{color:"#C47A21"}
});
