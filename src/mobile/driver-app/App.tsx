import { StatusBar } from "expo-status-bar";
import { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet,
  Text, View
} from "react-native";

const API_BASE_URL = "http://10.0.2.2:5000"; // Android emulator; use your computer's LAN IP on a physical phone.
const DRIVER_ID = "DRV-DEMO-004";
const ZONE_ID = "hyderabad-central";
const SERVICE_CLASS = "auto";

type QueueEntry = {
  driverId: string;
  zoneId: string;
  serviceClass: string;
  position: number;
  joinedAt: string;
};

const tabs = ["Home", "Queue", "Trips", "Profile"] as const;
type Tab = typeof tabs[number];

export default function App() {
  const [tab, setTab] = useState<Tab>("Home");
  const [queue, setQueue] = useState<QueueEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [online, setOnline] = useState(true);
  const [message, setMessage] = useState("Ready for your next turn.");
  const [error, setError] = useState("");

  const loadQueue = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const response = await fetch(`${API_BASE_URL}/api/v1/zones/${ZONE_ID}/queues/${SERVICE_CLASS}`);
      if (!response.ok) throw new Error("Could not load queue. Check that the API is running.");
      setQueue(await response.json());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to connect to TurnOva API.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadQueue(); }, [loadQueue]);

  const joinQueue = async () => {
    setLoading(true);
    setError("");
    try {
      const response = await fetch(`${API_BASE_URL}/api/v1/queues/join`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ driverId: DRIVER_ID, zoneId: ZONE_ID, serviceClass: SERVICE_CLASS })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message ?? "Unable to join queue.");
      setMessage(result.message ?? "You joined the queue.");
      await loadQueue();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to join queue.");
    } finally {
      setLoading(false);
    }
  };

  const myPosition = queue.find(x => x.driverId === DRIVER_ID)?.position;

  return (
    <SafeAreaView style={styles.safe}>
      <StatusBar style="light" />
      <View style={styles.header}>
        <View><Text style={styles.brand}>TurnOva</Text><Text style={styles.tagline}>Everyone’s Turn Matters.</Text></View>
        <View style={styles.demoPill}><Text style={styles.demoText}>DEMO</Text></View>
      </View>
      <ScrollView contentContainerStyle={styles.content}>
        {tab === "Home" && <>
          <Text style={styles.eyebrow}>DRIVER PARTNER</Text>
          <Text style={styles.title}>Good day, Driver</Text>
          <Text style={styles.sub}>Your next opportunity starts with a fair turn.</Text>
          <View style={styles.card}>
            <View style={styles.row}><Text style={styles.cardLabel}>DRIVER AVAILABILITY</Text><Text style={online ? styles.status : styles.offline}>{online ? "ONLINE" : "OFFLINE"}</Text></View>
            <Pressable style={styles.availability} onPress={() => setOnline(!online)}><Text style={styles.availabilityText}>{online ? "Go offline" : "Go online"}</Text></Pressable>
            <View style={styles.line} />
            <Text style={styles.cardLabel}>YOUR SERVICE ZONE</Text>
            <Text style={styles.cardTitle}>Hyderabad Central</Text>
            <Text style={styles.muted}>Auto · Standard queue</Text>
            <View style={styles.line} />
            <Text style={styles.cardLabel}>CURRENT QUEUE POSITION</Text>
            <Text style={styles.position}>{myPosition ? `#${myPosition}` : "—"}</Text>
            <Text style={styles.muted}>{myPosition ? "You are in the queue" : "Join the queue to get started"}</Text>
            <Pressable style={styles.primary} onPress={() => setTab("Queue")}><Text style={styles.primaryText}>{myPosition ? "View my queue" : "Join the queue"}</Text></Pressable>
          </View>
          <View style={styles.infoCard}><Text style={styles.infoTitle}>Fair by design</Text><Text style={styles.muted}>TurnOva considers eligible drivers in first-in, first-out order within their service zone.</Text></View>
        </>}
        {tab === "Queue" && <>
          <Text style={styles.eyebrow}>LIVE QUEUE · DEMO DATA</Text>
          <Text style={styles.title}>Your queue</Text>
          <Text style={styles.sub}>Hyderabad Central · Auto</Text>
          <View style={styles.card}>
            <View style={styles.row}><Text style={styles.cardTitle}>Queue status</Text><Pressable onPress={loadQueue}><Text style={styles.link}>Refresh</Text></Pressable></View>
            <Text style={styles.position}>{myPosition ? `#${myPosition}` : "Not joined"}</Text>
            <Text style={styles.muted}>{myPosition ? "Your current position" : "You are not in this queue yet."}</Text>
            <Pressable style={styles.primary} onPress={() => myPosition ? loadQueue() : joinQueue()} disabled={loading || !online}><Text style={styles.primaryText}>{loading ? "Please wait…" : myPosition ? "Refresh my position" : online ? "Join FIFO queue" : "Go online to join"}</Text></Pressable>
          </View>
          <Text style={styles.sectionTitle}>Drivers in queue</Text>
          {queue.map(item => <View key={item.driverId} style={styles.queueItem}>
            <View style={styles.rank}><Text style={styles.rankText}>{item.position}</Text></View>
            <View style={{ flex: 1 }}><Text style={styles.driverName}>{item.driverId}</Text><Text style={styles.muted}>Joined {new Date(item.joinedAt).toLocaleTimeString()}</Text></View>
            {item.driverId === DRIVER_ID && <Text style={styles.you}>YOU</Text>}
          </View>)}
          {queue.length === 0 && <Text style={styles.muted}>No drivers in this queue yet.</Text>}
        </>}
        {tab === "Trips" && <><Text style={styles.eyebrow}>YOUR ACTIVITY</Text><Text style={styles.title}>Trips</Text><View style={styles.infoCard}><Text style={styles.infoTitle}>No active trips</Text><Text style={styles.muted}>Accepted ride offers and trip history will appear here.</Text></View></>}
        {tab === "Profile" && <><Text style={styles.eyebrow}>ACCOUNT</Text><Text style={styles.title}>Driver profile</Text><View style={styles.card}><Text style={styles.cardTitle}>Demo Driver</Text><Text style={styles.muted}>Driver ID · {DRIVER_ID}</Text><View style={styles.line}/><Text style={styles.cardLabel}>APPROVAL STATUS</Text><Text style={styles.status}>Demo approved</Text><Text style={styles.muted}>Profile and verification are simulated for this MVP walkthrough.</Text></View></>}
        {loading && <ActivityIndicator style={{ marginTop: 14 }} color="#F4A340" />}
        {!!error && <Text style={styles.error}>{error}</Text>}
        {!!message && tab === "Queue" && <Text style={styles.success}>{message}</Text>}
      </ScrollView>
      <View style={styles.tabBar}>{tabs.map(item => <Pressable key={item} style={styles.tab} onPress={() => setTab(item)}><Text style={[styles.tabText, tab === item && styles.tabActive]}>{item}</Text></Pressable>)}</View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: "#F4F6F8" },
  header: { backgroundColor: "#14283F", paddingHorizontal: 22, paddingTop: 18, paddingBottom: 20, flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  brand: { color: "#FFFFFF", fontSize: 27, fontWeight: "800" },
  tagline: { color: "#B8C5D2", marginTop: 2, fontSize: 12 },
  demoPill: { borderRadius: 20, backgroundColor: "#30445A", paddingHorizontal: 12, paddingVertical: 7 },
  demoText: { color: "#F7B65F", fontSize: 10, fontWeight: "800", letterSpacing: 1 },
  content: { padding: 22, paddingBottom: 34 },
  eyebrow: { color: "#D58B32", fontWeight: "800", fontSize: 11, letterSpacing: 1.4, marginTop: 8 },
  title: { color: "#172B42", fontSize: 29, fontWeight: "800", marginTop: 8 },
  sub: { color: "#718094", fontSize: 14, marginTop: 6, marginBottom: 20 },
  card: { backgroundColor: "#FFFFFF", padding: 20, borderRadius: 20, marginBottom: 16, shadowColor: "#152B45", shadowOpacity: 0.06, shadowRadius: 12, elevation: 2 },
  cardLabel: { color: "#8290A0", fontSize: 10, fontWeight: "800", letterSpacing: 1.2 },
  cardTitle: { color: "#172B42", fontSize: 19, fontWeight: "800", marginTop: 7 },
  muted: { color: "#758397", fontSize: 13, lineHeight: 19, marginTop: 5 },
  line: { height: 1, backgroundColor: "#E9EDF1", marginVertical: 18 },
  position: { color: "#D98B2D", fontSize: 42, fontWeight: "900", marginTop: 8 },
  primary: { backgroundColor: "#E89A3B", borderRadius: 13, paddingVertical: 14, alignItems: "center", marginTop: 18 },
  primaryText: { color: "#172B42", fontWeight: "800", fontSize: 14 },
  infoCard: { backgroundColor: "#EAF0F5", padding: 18, borderRadius: 16, marginTop: 4 },
  infoTitle: { color: "#172B42", fontSize: 16, fontWeight: "800" },
  row: { flexDirection: "row", justifyContent: "space-between", alignItems: "center" },
  link: { color: "#C47A21", fontWeight: "800" },
  availability: { alignSelf: "flex-start", backgroundColor: "#F0F3F6", borderRadius: 10, paddingHorizontal: 12, paddingVertical: 8, marginTop: 9 },
  availabilityText: { color: "#30445A", fontWeight: "800", fontSize: 12 },
  sectionTitle: { color: "#172B42", fontSize: 17, fontWeight: "800", marginTop: 10, marginBottom: 10 },
  queueItem: { backgroundColor: "#FFFFFF", borderRadius: 14, padding: 13, marginBottom: 9, flexDirection: "row", alignItems: "center", gap: 12 },
  rank: { width: 34, height: 34, borderRadius: 11, backgroundColor: "#F9EBD8", alignItems: "center", justifyContent: "center" },
  rankText: { color: "#B66D1D", fontWeight: "900" },
  driverName: { color: "#20354B", fontWeight: "800" },
  you: { color: "#B66D1D", fontSize: 10, fontWeight: "900" },
  status: { color: "#21845A", fontWeight: "800", marginTop: 7, marginBottom: 8 },
  offline: { color: "#B54708", fontWeight: "800", fontSize: 10 },
  error: { color: "#B42318", marginTop: 12, fontSize: 13 },
  success: { color: "#237A54", marginTop: 10, fontSize: 13 },
  tabBar: { flexDirection: "row", backgroundColor: "#FFFFFF", borderTopWidth: 1, borderTopColor: "#E7EBEF", paddingTop: 10, paddingBottom: 16 },
  tab: { flex: 1, alignItems: "center", paddingVertical: 7 },
  tabText: { color: "#8793A2", fontSize: 12, fontWeight: "700" },
  tabActive: { color: "#C47A21" }
});`