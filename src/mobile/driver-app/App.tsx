import { StatusBar } from "expo-status-bar";
import * as SecureStore from "expo-secure-store";
import * as Location from "expo-location";
import * as FileSystem from "expo-file-system/legacy";
import * as Sharing from "expo-sharing";
import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";
import { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";

const API = process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://10.0.2.2:5000";
const ZONE = "hyderabad-central";
const SERVICE = "auto";
type Tokens = { accessToken: string; refreshToken: string };
type User = { id: string; email: string; name?: string; role: string };
type QueueEntry = { driverId: string; position: number; joinedAt: string };
type QueueMembership = { isQueued: boolean; position: number | null };

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
  const [tab, setTab] = useState<"Home" | "Queue" | "Trips" | "Payslips" | "Profile">("Home");
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [queue, setQueue] = useState<QueueEntry[]>([]);
  const [membership, setMembership] = useState<QueueMembership>({ isQueued: false, position: null });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [availableRides, setAvailableRides] = useState<any[]>([]);
  const [activeRide, setActiveRide] = useState<any>(null);
  const [onboardingProfile, setOnboardingProfile] = useState<any>(null);
  const [driverName, setDriverName] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [vehicleMake, setVehicleMake] = useState("");
  const [vehicleModel, setVehicleModel] = useState("");
  const [vehicleRegistration, setVehicleRegistration] = useState("");
  const [licenceReference, setLicenceReference] = useState("");
  const [registrationReference, setRegistrationReference] = useState("");

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
      let accessToken: string;
      try {
        accessToken = await refreshSession(refreshToken);
      } catch {
        await clearTokens();
        setTokens(null);
        setUser(null);
        throw new Error("Session expired. Please sign in again.");
      }
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
      const [data, current] = await Promise.all([
        apiFetch(`/api/v1/zones/${ZONE}/queues/${SERVICE}`),
        apiFetch(`/api/v1/zones/${ZONE}/queues/${SERVICE}/me`)
      ]);
      setQueue(data);
      setMembership(current);
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to load queue."); }
  }, [apiFetch]);
  useEffect(() => { if (user && tab === "Queue") void loadQueue(); }, [user, tab, loadQueue]);

  const joinQueue = async () => {
    if (!user) return;
    setLoading(true); setError("");
    try {
      const result = await apiFetch("/api/v1/queues/join", {
        method: "POST",
        body: JSON.stringify({ zoneId: ZONE, serviceClass: SERVICE })
      });
      setNotice(result.message ?? "You joined the queue.");
      await loadQueue();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to join queue."); }
    finally { setLoading(false); }
  };

  const leaveQueue = async () => {
    setLoading(true); setError(""); setNotice("");
    try {
      await apiFetch(`/api/v1/zones/${ZONE}/queues/${SERVICE}/me`, { method: "DELETE" });
      setNotice("You left the queue. You can join again whenever you're ready.");
      await loadQueue();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to leave queue."); }
    finally { setLoading(false); }
  };

  const loadAvailableRides = async () => { try { setAvailableRides(await apiFetch("/api/v1/driver/rides/available")); } catch (e) { setError(e instanceof Error ? e.message : "Unable to load ride requests."); } };
  const acceptRide = async (rideId: string) => { try { const accepted = await apiFetch(`/api/v1/rides/${rideId}/accept`, { method: "POST" }); setActiveRide(accepted); setNotice("Ride accepted. Location sharing is active while this screen is open."); } catch (e) { setError(e instanceof Error ? e.message : "Unable to accept ride."); } };
  useEffect(() => {
    if (!user || tab !== "Trips" || !activeRide) return;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API}/hubs/rides?rideId=${encodeURIComponent(activeRide.rideId)}`, {
        accessTokenFactory: async () => await SecureStore.getItemAsync("turnova.access") ?? tokens?.accessToken ?? "",
        transport: HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on("RideStatusChanged", (update: { rideId: string; status: string }) => {
      if (update.rideId === activeRide.rideId) setActiveRide((current: any) => current ? { ...current, status: update.status, locationSharingActive: ["Accepted", "Arrived", "InProgress"].includes(update.status) } : current);
    });
    void connection.start().catch(() => {});
    return () => { void connection.stop(); };
  }, [user, tab, activeRide?.rideId, tokens?.accessToken]);

  useEffect(() => { if (!user || tab !== "Trips" || !activeRide || !activeRide.locationSharingActive || !["Accepted", "Arrived", "InProgress"].includes(activeRide.status)) return; let subscription: Location.LocationSubscription | undefined; let mounted = true; (async () => { const permission = await Location.requestForegroundPermissionsAsync(); if (!permission.granted) { setError("Location permission is required to share your position with the rider."); return; } subscription = await Location.watchPositionAsync({ accuracy: Location.Accuracy.High, timeInterval: 5000, distanceInterval: 10 }, location => { if (mounted) void apiFetch(`/api/v1/rides/${activeRide.rideId}/location`, { method: "POST", body: JSON.stringify({ latitude: location.coords.latitude, longitude: location.coords.longitude }) }).catch(e => setError(e instanceof Error ? e.message : "Unable to update location.")); }); })(); return () => { mounted = false; subscription?.remove(); }; }, [user, tab, activeRide?.rideId, activeRide?.status, activeRide?.locationSharingActive, apiFetch]);

  const advanceRide = async (action: "arrive" | "start" | "complete") => { if (!activeRide) return; try { const updated = await apiFetch(`/api/v1/rides/${activeRide.rideId}/${action}`, { method: "POST" }); setActiveRide(updated); setNotice(action === "arrive" ? "Arrival confirmed." : action === "start" ? "Trip started." : "Trip completed. Location sharing stopped."); } catch (e) { setError(e instanceof Error ? e.message : "Unable to update ride status."); } };


  const downloadPayslip = async (year: number, month: number) => {
    if (!tokens) return;
    setLoading(true); setError(""); setNotice("");
    try {
      let accessToken = tokens.accessToken;
      let response = await fetch(`${API}/api/v1/driver/payslips/${year}/${month}.pdf`, {
        headers: { Authorization: `Bearer ${accessToken}` }
      });
      if (response.status === 401) {
        const refreshToken = await SecureStore.getItemAsync("turnova.refresh");
        if (!refreshToken) throw new Error("Session expired. Please sign in again.");
        accessToken = await refreshSession(refreshToken);
        response = await fetch(`${API}/api/v1/driver/payslips/${year}/${month}.pdf`, {
          headers: { Authorization: `Bearer ${accessToken}` }
        });
      }
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.message ?? body.title ?? "Payslip is not available yet.");
      }
      const bytes = new Uint8Array(await response.arrayBuffer());
      const binary = Array.from(bytes, byte => String.fromCharCode(byte)).join("");
      const base64 = btoa(binary);
      const filename = `TurnOva-Payslip-${year}-${String(month).padStart(2, "0")}.pdf`;
      const uri = `${FileSystem.cacheDirectory}${filename}`;
      await FileSystem.writeAsStringAsync(uri, base64, { encoding: FileSystem.EncodingType.Base64 });
      if (await Sharing.isAvailableAsync()) {
        await Sharing.shareAsync(uri, { mimeType: "application/pdf", dialogTitle: filename });
        setNotice(`PDF payslip for ${month}/${year} is ready to save or share.`);
      } else {
        setNotice(`PDF saved to the app cache: ${filename}`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to download payslip.");
    } finally {
      setLoading(false);
    }
  };

  const loadOnboarding = useCallback(async () => {
    try {
      const profile = await apiFetch("/api/v1/driver/onboarding/me");
      if (profile?.driverId) {
        setOnboardingProfile(profile);
        setDriverName(profile.fullName ?? "");
        setPhoneNumber(profile.phoneNumber ?? "");
        setVehicleMake(profile.vehicleMake ?? "");
        setVehicleModel(profile.vehicleModel ?? "");
        setVehicleRegistration(profile.vehicleRegistration ?? "");
        setLicenceReference(profile.documents?.find((document: any) => document.documentType === "DrivingLicence")?.storageReference ?? "");
        setRegistrationReference(profile.documents?.find((document: any) => document.documentType === "VehicleRegistration")?.storageReference ?? "");
      }
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to load onboarding profile."); }
  }, [apiFetch]);

  useEffect(() => { if (user && tab === "Profile") void loadOnboarding(); }, [user, tab, loadOnboarding]);

  const submitOnboarding = async () => {
    setLoading(true); setError(""); setNotice("");
    try {
      const profile = await apiFetch("/api/v1/driver/onboarding", {
        method: "POST",
        body: JSON.stringify({
          zoneId: ZONE,
          fullName: driverName,
          phoneNumber,
          vehicleMake,
          vehicleModel,
          vehicleRegistration,
          serviceClass: SERVICE,
          passengerCapacity: 3,
          documents: [
            { documentType: "DrivingLicence", storageReference: licenceReference },
            { documentType: "VehicleRegistration", storageReference: registrationReference }
          ]
        })
      });
      setOnboardingProfile(profile);
      setNotice("Onboarding submitted. Queue and ride access will unlock after approval.");
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to submit onboarding."); }
    finally { setLoading(false); }
  };

  const logout = async () => {
    try { if (tokens) await jsonRequest("/api/v1/auth/logout", { method: "POST" }, tokens.accessToken); }
    catch { /* Local credentials are cleared even if the network is unavailable. */ }
    await clearTokens(); setTokens(null); setUser(null); setQueue([]); setMembership({ isQueued: false, position: null });
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

  const position = membership.position;
  const tabs = ["Home", "Queue", "Trips", "Payslips", "Profile"] as const;
  return <SafeAreaView style={styles.safe}><StatusBar style="light" />
    <View style={styles.header}><View><Text style={styles.brand}>TurnOva</Text><Text style={styles.tagline}>Driver partner · {user.name || user.email}</Text></View><Text style={styles.role}>DRIVER</Text></View>
    <ScrollView contentContainerStyle={styles.content}>
      {tab === "Home" && <><Text style={styles.eyebrow}>YOUR DRIVER SPACE</Text><Text style={styles.title}>Welcome, {user.name || "Driver"}</Text><Text style={styles.sub}>Your next opportunity starts with a fair turn.</Text><View style={styles.card}><Text style={styles.label}>SERVICE ZONE</Text><Text style={styles.cardTitle}>Hyderabad Central · Auto</Text><Text style={styles.label}>YOUR QUEUE POSITION</Text><Text style={styles.position}>{position ? `#${position}` : "—"}</Text><Pressable style={styles.primary} onPress={() => setTab("Queue")}><Text style={styles.primaryText}>View queue</Text></Pressable></View></>}
      {tab === "Queue" && <><Text style={styles.eyebrow}>HYDERABAD CENTRAL · AUTO</Text><Text style={styles.title}>Your queue</Text><View style={styles.card}><Text style={styles.label}>CURRENT POSITION</Text><Text style={styles.position}>{position ? `#${position}` : "Not joined"}</Text><Pressable style={styles.primary} onPress={() => void (membership.isQueued ? loadQueue() : joinQueue())} disabled={loading}><Text style={styles.primaryText}>{loading ? "Please wait…" : membership.isQueued ? "Refresh queue" : "Join FIFO queue"}</Text></Pressable>{membership.isQueued && <Pressable style={styles.secondary} onPress={() => void leaveQueue()} disabled={loading}><Text style={styles.secondaryText}>Leave queue</Text></Pressable>}<Pressable onPress={() => void loadQueue()}><Text style={styles.link}>Refresh list</Text></Pressable></View>{queue.map(item => <View style={styles.queueItem} key={item.driverId}><Text style={styles.cardTitle}>#{item.position} · {item.driverId}</Text><Text style={styles.muted}>{new Date(item.joinedAt).toLocaleString()}</Text></View>)}</>}
      {tab === "Trips" && <><Text style={styles.title}>Ride requests</Text><View style={styles.card}><Pressable style={styles.primary} onPress={() => void loadAvailableRides()}><Text style={styles.primaryText}>Refresh requests</Text></Pressable>{availableRides.map(item=><View key={item.rideId} style={{marginTop:14}}><Text style={styles.cardTitle}>{item.pickup} → {item.dropoff}</Text><Text style={styles.muted}>{item.passengers} passenger(s) · {item.rideId}</Text><Pressable style={styles.secondary} onPress={()=>void acceptRide(item.rideId)}><Text style={styles.secondaryText}>Accept ride</Text></Pressable></View>)}{activeRide&&<><Text style={styles.success}>{activeRide.status} · {activeRide.rideId} · {activeRide.locationSharingActive?"Location sharing active":"Location sharing stopped"}</Text>{activeRide.status==="Accepted"&&<Pressable style={styles.secondary} onPress={()=>void advanceRide("arrive")}><Text style={styles.secondaryText}>Mark arrived</Text></Pressable>}{activeRide.status==="Arrived"&&<Pressable style={styles.secondary} onPress={()=>void advanceRide("start")}><Text style={styles.secondaryText}>Start trip</Text></Pressable>}{activeRide.status==="InProgress"&&<Pressable style={styles.secondary} onPress={()=>void advanceRide("complete")}><Text style={styles.secondaryText}>Complete trip</Text></Pressable>}</>}</View></>}
      {tab === "Payslips" && <><Text style={styles.eyebrow}>MONTHLY PDF DOCUMENTS</Text><Text style={styles.title}>Your payslips</Text><Text style={styles.sub}>Download a completed month's payslip as a PDF. Only finalized months are available.</Text><View style={styles.card}><Text style={styles.cardTitle}>Recent months</Text>{Array.from({ length: 12 }, (_, index) => { const date = new Date(); date.setDate(1); date.setMonth(date.getMonth() - index - 1); const year = date.getFullYear(); const month = date.getMonth() + 1; const label = date.toLocaleDateString(undefined, { month: "long", year: "numeric" }); return <View key={`${year}-${month}`} style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between", paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: "#EDF0F3" }}><Text style={styles.cardTitle}>{label}</Text><Pressable style={styles.secondary} disabled={loading} onPress={() => void downloadPayslip(year, month)}><Text style={styles.secondaryText}>Download PDF</Text></Pressable></View>; })}</View></>}
      {tab === "Profile" && <><Text style={styles.title}>Driver onboarding</Text><View style={styles.card}>
        <Text style={styles.cardTitle}>Verification status: {onboardingProfile?.status ?? "Not submitted"}</Text>
        {!!onboardingProfile?.reviewReason && <Text style={styles.error}>Review note: {onboardingProfile.reviewReason}</Text>}
        {onboardingProfile?.status === "Approved" ? <Text style={styles.success}>Your profile is approved. Queue and ride access are enabled.</Text> : onboardingProfile?.status === "Suspended" ? <Text style={styles.error}>Your driver account is suspended. Contact TurnOva operations for assistance.</Text> : <>
          <Text style={styles.sub}>Complete your driver and vehicle details. Your application will remain pending until reviewed.</Text>
          <TextInput value={driverName} onChangeText={setDriverName} style={styles.input} placeholder="Full legal name" />
          <TextInput value={phoneNumber} onChangeText={setPhoneNumber} style={styles.input} placeholder="Phone number with country code" keyboardType="phone-pad" />
          <TextInput value={vehicleMake} onChangeText={setVehicleMake} style={styles.input} placeholder="Vehicle make (e.g. Bajaj)" />
          <TextInput value={vehicleModel} onChangeText={setVehicleModel} style={styles.input} placeholder="Vehicle model" />
          <TextInput value={vehicleRegistration} onChangeText={setVehicleRegistration} style={styles.input} placeholder="Vehicle registration number" autoCapitalize="characters" />
          <Text style={styles.label}>PRIVATE DOCUMENT STORAGE REFERENCES</Text>
          <Text style={styles.muted}>Upload files to your approved private storage workflow first. Enter opaque object references, not public URLs.</Text>
          <TextInput value={licenceReference} onChangeText={setLicenceReference} style={styles.input} placeholder="Driving licence object reference" autoCapitalize="none" />
          <TextInput value={registrationReference} onChangeText={setRegistrationReference} style={styles.input} placeholder="Vehicle RC object reference" autoCapitalize="none" />
          <Pressable style={styles.primary} disabled={loading || !driverName.trim() || !phoneNumber.trim() || !vehicleMake.trim() || !vehicleModel.trim() || !vehicleRegistration.trim() || !licenceReference.trim() || !registrationReference.trim()} onPress={() => void submitOnboarding()}><Text style={styles.primaryText}>{loading ? "Please wait…" : onboardingProfile ? "Resubmit for review" : "Submit for approval"}</Text></Pressable>
        </>}
      </View><View style={styles.card}><Text style={styles.cardTitle}>{user.name || "Driver"}</Text><Text style={styles.muted}>{user.email}</Text><Text style={styles.muted}>Account role: {user.role}</Text><Pressable style={styles.secondary} onPress={() => void logout()}><Text style={styles.secondaryText}>Sign out</Text></Pressable></View></>}
      {!!error && <Text style={styles.error}>{error}</Text>}{!!notice && <Text style={styles.success}>{notice}</Text>}
    </ScrollView>
    <View style={styles.tabBar}>{tabs.map(item => <Pressable key={item} style={styles.tab} onPress={() => setTab(item)}><Text style={[styles.tabText, tab === item && styles.tabActive]}>{item}</Text></Pressable>)}</View>
  </SafeAreaView>;
}

const styles = StyleSheet.create({
  safe:{flex:1,backgroundColor:"#F4F6F8"},header:{backgroundColor:"#14283F",padding:22,paddingTop:18},brand:{color:"#FFF",fontSize:25,fontWeight:"800"},tagline:{color:"#B8C5D2",marginTop:4,fontSize:12},role:{color:"#F7B65F",fontWeight:"800",marginTop:6},content:{padding:22,paddingBottom:34},eyebrow:{color:"#D58B32",fontWeight:"800",fontSize:11,letterSpacing:1.3,marginTop:8},title:{color:"#172B42",fontSize:28,fontWeight:"800",marginTop:8},sub:{color:"#718094",fontSize:14,marginTop:6,marginBottom:20},card:{backgroundColor:"#FFF",padding:20,borderRadius:18,marginBottom:15},input:{borderWidth:1,borderColor:"#E2E8EE",borderRadius:12,padding:14,color:"#20354B",marginBottom:12},primary:{backgroundColor:"#E89A3B",borderRadius:12,padding:15,alignItems:"center",marginTop:12},primaryText:{color:"#172B42",fontWeight:"800"},link:{color:"#B66D1D",fontWeight:"800",textAlign:"center",marginTop:18},label:{color:"#8290A0",fontSize:10,fontWeight:"800",letterSpacing:1.1,marginTop:10},cardTitle:{color:"#172B42",fontSize:18,fontWeight:"800",marginVertical:8},position:{color:"#D98B2D",fontSize:42,fontWeight:"900",marginTop:6},muted:{color:"#758397",fontSize:13,marginTop:5},queueItem:{backgroundColor:"#FFF",padding:14,borderRadius:12,marginBottom:8},error:{color:"#B42318",marginTop:12},success:{color:"#237A54",marginTop:12},secondary:{borderWidth:1,borderColor:"#D7DEE6",borderRadius:12,padding:13,alignItems:"center",marginTop:20},secondaryText:{color:"#30445A",fontWeight:"800"},tabBar:{flexDirection:"row",backgroundColor:"#FFF",paddingVertical:14},tab:{flex:1,alignItems:"center"},tabText:{color:"#8793A2",fontSize:12,fontWeight:"700"},tabActive:{color:"#C47A21"}
});
