import { StatusBar } from "expo-status-bar";
import { useState } from "react";
import {
  ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet,
  Text, TextInput, View
} from "react-native";

const API_BASE_URL = "http://10.0.2.2:5000"; // Android emulator; use your computer's LAN IP on a physical phone.
type Tab = "Book" | "Activity" | "Offers" | "Profile";
type Ride = { rideId: string; pickup: string; dropoff: string; passengers: number; status: string; estimatedFareInr: number };

export default function App() {
  const [tab, setTab] = useState<Tab>("Book");
  const [pickup, setPickup] = useState("KPHB, Hyderabad");
  const [dropoff, setDropoff] = useState("HITEC City, Hyderabad");
  const [passengers, setPassengers] = useState("1");
  const [ride, setRide] = useState<Ride | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const requestRide = async () => {
    setLoading(true);
    setError("");
    try {
      const response = await fetch(`${API_BASE_URL}/api/v1/rides/request`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ pickup, dropoff, passengers: Number(passengers) || 1 })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.message ?? "Unable to request ride.");
      setRide(result);
      setTab("Activity");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to connect to TurnOva API.");
    } finally {
      setLoading(false);
    }
  };

  const tabs: Tab[] = ["Book", "Activity", "Offers", "Profile"];

  return (
    <SafeAreaView style={styles.safe}>
      <StatusBar style="light" />
      <View style={styles.header}>
        <View><Text style={styles.brand}>TurnOva</Text><Text style={styles.tagline}>Everyone’s Turn Matters.</Text></View>
        <View style={styles.demoPill}><Text style={styles.demoText}>DEMO</Text></View>
      </View>
      <ScrollView contentContainerStyle={styles.content}>
        {tab === "Book" && <>
          <Text style={styles.eyebrow}>YOUR CITY, YOUR RIDE</Text>
          <Text style={styles.title}>Where to?</Text>
          <Text style={styles.sub}>A fair ride starts with a simple booking.</Text>
          <View style={styles.card}>
            <Text style={styles.label}>PICKUP LOCATION</Text>
            <TextInput value={pickup} onChangeText={setPickup} style={styles.input} placeholder="Enter pickup location" placeholderTextColor="#9AA5B2"/>
            <View style={styles.routeLine}><View style={styles.dotStart}/><View style={styles.routeDash}/><View style={styles.dotEnd}/></View>
            <Text style={styles.label}>DROP-OFF LOCATION</Text>
            <TextInput value={dropoff} onChangeText={setDropoff} style={styles.input} placeholder="Where are you going?" placeholderTextColor="#9AA5B2"/>
            <View style={styles.divider}/>
            <Text style={styles.label}>PASSENGERS</Text>
            <View style={styles.passengerRow}>
              <Text style={styles.muted}>Number of passengers</Text>
              <View style={styles.stepper}>
                <Pressable onPress={() => setPassengers(String(Math.max(1, Number(passengers) - 1)))} style={styles.step}><Text style={styles.stepText}>−</Text></Pressable>
                <Text style={styles.count}>{passengers}</Text>
                <Pressable onPress={() => setPassengers(String(Math.min(6, Number(passengers) + 1)))} style={styles.step}><Text style={styles.stepText}>+</Text></Pressable>
              </View>
            </View>
            <Pressable style={styles.primary} onPress={requestRide} disabled={loading || !pickup.trim() || !dropoff.trim()}>
              <Text style={styles.primaryText}>{loading ? "Requesting…" : "Find my ride"}</Text>
            </Pressable>
            {loading && <ActivityIndicator style={{ marginTop: 12 }} color="#E89A3B"/>}
            {!!error && <Text style={styles.error}>{error}</Text>}
          </View>
          <View style={styles.infoCard}><Text style={styles.infoTitle}>Fair turns. Clear choices.</Text><Text style={styles.muted}>TurnOva uses a transparent zone-based queue to offer rides to eligible drivers in FIFO order.</Text></View>
        </>}
        {tab === "Activity" && <>
          <Text style={styles.eyebrow}>YOUR RIDE</Text><Text style={styles.title}>Activity</Text>
          {ride ? <View style={styles.card}>
            <View style={styles.statusPill}><Text style={styles.statusText}>{ride.status.toUpperCase()}</Text></View>
            <Text style={styles.cardTitle}>Ride requested</Text><Text style={styles.muted}>Reference · {ride.rideId}</Text>
            <View style={styles.divider}/>
            <Text style={styles.label}>PICKUP</Text><Text style={styles.location}>{ride.pickup}</Text>
            <Text style={styles.label}>DROP-OFF</Text><Text style={styles.location}>{ride.dropoff}</Text>
            <Text style={styles.muted}>{ride.passengers} passenger(s) · Demo estimate ₹{ride.estimatedFareInr}</Text>
            <Text style={styles.demoNote}>Demo request only — no driver has been assigned and no payment has been taken.</Text>
          </View> : <View style={styles.infoCard}><Text style={styles.infoTitle}>No rides yet</Text><Text style={styles.muted}>Your ride requests will appear here.</Text></View>}
        </>}
        {tab === "Offers" && <><Text style={styles.eyebrow}>RIDE OPTIONS</Text><Text style={styles.title}>Offers</Text><View style={styles.infoCard}><Text style={styles.infoTitle}>Options coming soon</Text><Text style={styles.muted}>Vehicle options and confirmed fare details will appear here once enabled.</Text></View></>}
        {tab === "Profile" && <><Text style={styles.eyebrow}>ACCOUNT</Text><Text style={styles.title}>Your profile</Text><View style={styles.card}><Text style={styles.cardTitle}>Welcome to TurnOva</Text><Text style={styles.muted}>Your rider profile and preferences will be available here.</Text><View style={styles.divider}/><Text style={styles.demoNote}>This is a demonstration profile.</Text></View></>}
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
  label: { color: "#8290A0", fontSize: 10, fontWeight: "800", letterSpacing: 1.2, marginBottom: 8 },
  input: { borderWidth: 1, borderColor: "#E2E8EE", backgroundColor: "#FAFBFC", borderRadius: 12, paddingHorizontal: 13, paddingVertical: 13, color: "#20354B", fontSize: 14, marginBottom: 12 },
  routeLine: { flexDirection: "row", alignItems: "center", marginVertical: 1, marginLeft: 7, height: 18 },
  dotStart: { width: 9, height: 9, borderRadius: 5, backgroundColor: "#26956C" },
  routeDash: { width: 2, height: 15, backgroundColor: "#D5DDE5", marginLeft: 3 },
  dotEnd: { width: 9, height: 9, borderRadius: 5, backgroundColor: "#E89A3B", marginLeft: 3 },
  divider: { height: 1, backgroundColor: "#E9EDF1", marginVertical: 16 },
  passengerRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between" },
  muted: { color: "#758397", fontSize: 13, lineHeight: 19 },
  stepper: { flexDirection: "row", alignItems: "center", gap: 14 },
  step: { width: 30, height: 30, borderRadius: 10, backgroundColor: "#F7EBD9", alignItems: "center", justifyContent: "center" },
  stepText: { color: "#B66D1D", fontWeight: "900", fontSize: 18 },
  count: { color: "#172B42", fontWeight: "800", fontSize: 15 },
  primary: { backgroundColor: "#E89A3B", borderRadius: 13, paddingVertical: 15, alignItems: "center", marginTop: 22 },
  primaryText: { color: "#172B42", fontWeight: "800", fontSize: 14 },
  infoCard: { backgroundColor: "#EAF0F5", padding: 18, borderRadius: 16, marginTop: 4 },
  infoTitle: { color: "#172B42", fontSize: 16, fontWeight: "800", marginBottom: 5 },
  statusPill: { alignSelf: "flex-start", backgroundColor: "#F9EBD8", borderRadius: 20, paddingHorizontal: 10, paddingVertical: 6 },
  statusText: { color: "#B66D1D", fontSize: 10, fontWeight: "900", letterSpacing: 1 },
  cardTitle: { color: "#172B42", fontSize: 19, fontWeight: "800", marginTop: 12 },
  location: { color: "#20354B", fontSize: 15, fontWeight: "700", marginBottom: 13 },
  demoNote: { color: "#8A6B42", fontSize: 12, lineHeight: 18, marginTop: 12 },
  error: { color: "#B42318", marginTop: 12, fontSize: 13 },
  tabBar: { flexDirection: "row", backgroundColor: "#FFFFFF", borderTopWidth: 1, borderTopColor: "#E7EBEF", paddingTop: 10, paddingBottom: 16 },
  tab: { flex: 1, alignItems: "center", paddingVertical: 7 },
  tabText: { color: "#8793A2", fontSize: 12, fontWeight: "700" },
  tabActive: { color: "#C47A21" }
});
