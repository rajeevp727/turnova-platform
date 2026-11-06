import { StatusBar } from "expo-status-bar";
import * as SecureStore from "expo-secure-store";
import { useCallback, useEffect, useRef, useState } from "react";
import { useColorScheme } from "react-native";
import { ActivityIndicator, Image, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import TurnOvaLogo from "./TurnOvaLogo";
import { singleFlight } from "../shared/refreshCoordinator";
import { canShareActiveRideLocation, findActiveRide, isActiveRide } from "../shared/rideRecovery";
import MapView, { Marker } from "react-native-maps";
import * as Location from "expo-location";
import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";

type ThemePreference = "light" | "dark" | "system";
type ThemeColors = {
  background: string; header: string; surface: string; text: string; muted: string;
  border: string; accent: string; accentText: string; subtle: string; label: string;
  danger: string; success: string; tabInactive: string; tabActive: string;
};
const lightColors: ThemeColors = {
  background: "#F4F6F8", header: "#14283F", surface: "#FFFFFF", text: "#172B42",
  muted: "#718094", border: "#E2E8EE", accent: "#087BFA", accentText: "#FFFFFF",
  subtle: "#B8C5D2", label: "#8290A0", danger: "#B42318", success: "#237A54",
  tabInactive: "#8793A2", tabActive: "#087BFA"
};
const darkColors: ThemeColors = {
  background: "#0F1722", header: "#101D2B", surface: "#182536", text: "#E8EEF5",
  muted: "#A6B3C2", border: "#344457", accent: "#18DDB9", accentText: "#06243D",
  subtle: "#A6B3C2", label: "#A6B3C2", danger: "#FF8A80", success: "#70D6A0",
  tabInactive: "#9AA9B9", tabActive: "#18DDB9"
};
const darkMapStyle = [
  { elementType: "geometry", stylers: [{ color: "#182536" }] },
  { elementType: "labels.text.fill", stylers: [{ color: "#A6B3C2" }] },
  { elementType: "labels.text.stroke", stylers: [{ color: "#101D2B" }] },
  { featureType: "road", elementType: "geometry", stylers: [{ color: "#344457" }] },
  { featureType: "water", elementType: "geometry", stylers: [{ color: "#0F1722" }] },
  { featureType: "poi", elementType: "geometry", stylers: [{ color: "#1D2C3E" }] }
];
const API = process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://10.0.2.2:5000";
type Tokens = { accessToken: string; refreshToken: string };
type User = { id: string; email: string; name?: string; role: string };
type ServiceClass = "cab";
type Ride = { rideId: string; pickup: string; dropoff: string; passengers: number; serviceClass: ServiceClass; status: string; estimatedFareInr: number | null; stops?: string[]; unionId?: string; unionName?: string; driverDisplayName?: string; driverVehicleMake?: string; driverVehicleModel?: string; driverDistanceToPickupKm?: number | null; negotiationExpiresAtUtc?: string | null; pickupLatitude?: number; pickupLongitude?: number; driverLatitude?: number; driverLongitude?: number; locationUpdatedAt?: string };
type DiscoveryDriver = { driverId: string; fullName: string; vehicleMake: string; vehicleModel: string; serviceClass: ServiceClass; passengerCapacity: number; distanceToPickupKm: number; profilePhotoBase64?: string | null; profilePhotoContentType?: string | null };
type DiscoveryUnion = { unionId: string; unionName: string; drivers: DiscoveryDriver[] };
type DiscoveryResult = { pickupZoneId: string; journeyDistanceKm: number | null; unions: DiscoveryUnion[] };
type RideHistoryItem = { rideId: string; pickup: string; dropoff: string; passengers: number; serviceClass: string; status: string; requestedAt: string; acceptedAt?: string | null };
type AgreementVersion = { rideId: string; version: number; amount: number; currency: string; pickup: string; dropoff: string; stops: string[]; proposedBy: string; createdAtUtc: string; riderConfirmed: boolean; driverConfirmed: boolean };
async function saveTokens(t: Tokens) { await SecureStore.setItemAsync("turnova.access", t.accessToken); await SecureStore.setItemAsync("turnova.refresh", t.refreshToken); }
async function clearTokens() { await SecureStore.deleteItemAsync("turnova.access"); await SecureStore.deleteItemAsync("turnova.refresh"); }
async function request(path: string, init: RequestInit = {}, token?: string) {
  const response = await fetch(`${API}${path}`, { ...init, headers: { "Content-Type":"application/json", ...(token ? { Authorization:`Bearer ${token}` } : {}), ...init.headers } });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.detail ?? body.message ?? body.title ?? "Request failed.");
  return body;
}

export default function App() {
  const [user,setUser]=useState<User|null>(null);
  const [themePreference, setThemePreference] = useState<ThemePreference>("system");
  const systemColorScheme = useColorScheme(); const [tokens,setTokens]=useState<Tokens|null>(null); const refreshInFlight=useRef<Promise<string>|null>(null);
  const [tab,setTab]=useState<"Book"|"Activity"|"Offers"|"Profile">("Book");
  const [mode,setMode]=useState<"login"|"register">("login"); const [email,setEmail]=useState(""); const [password,setPassword]=useState(""); const [name,setName]=useState("");
  const [pickup,setPickup]=useState("Current location"); const [dropoff,setDropoff]=useState("HITEC City, Hyderabad"); const [stopsText,setStopsText]=useState(""); const [passengers,setPassengers]=useState("1");
  const [ride,setRide]=useState<Ride|null>(null); const [rideHistory,setRideHistory]=useState<RideHistoryItem[]>([]); const [loading,setLoading]=useState(true); const [error,setError]=useState("");
  const [discovery,setDiscovery]=useState<DiscoveryResult|null>(null);
  const [selectedDriverId,setSelectedDriverId]=useState<string|null>(null);
  const [agreementHistory,setAgreementHistory]=useState<AgreementVersion[]>([]);
  const [agreementAmount,setAgreementAmount]=useState("");
  const [pickupCoordinates,setPickupCoordinates]=useState<{latitude:number;longitude:number}|null>(null);
  const [bookingRequestKey,setBookingRequestKey]=useState(()=>`${Date.now()}-${Math.random().toString(36).slice(2)}`);
  useEffect(() => {
    let mounted = true;
    void SecureStore.getItemAsync("turnova.theme").then(value => {
      if (mounted && (value === "light" || value === "dark" || value === "system")) setThemePreference(value);
    }).catch(() => {});
    return () => { mounted = false; };
  }, []);

  const selectTheme = (preference: ThemePreference) => {
    setThemePreference(preference);
    void SecureStore.setItemAsync("turnova.theme", preference);
  };

  const accept=useCallback(async(result:any)=>{const t={accessToken:result.accessToken,refreshToken:result.refreshToken};await saveTokens(t);setTokens(t);setUser(result.user);setError("");},[]);
  const refresh=useCallback(async(rt:string)=>{const result=await request("/api/v1/auth/refresh",{method:"POST",body:JSON.stringify({refreshToken:rt})});await accept(result);return result.accessToken as string;},[accept]);
  const apiFetch=useCallback(async(path:string,init:RequestInit={})=>{
    if(!tokens)throw new Error("Please sign in again.");
    let response=await fetch(`${API}${path}`,{...init,headers:{"Content-Type":"application/json",Authorization:`Bearer ${tokens.accessToken}`,...init.headers}});
    if(response.status===401){const rt=await SecureStore.getItemAsync("turnova.refresh");if(!rt){await clearTokens();setTokens(null);setUser(null);throw new Error("Session expired. Please sign in again.");}let accessToken:string;try{accessToken=await singleFlight(refreshInFlight,()=>refresh(rt));}catch{await clearTokens();setTokens(null);setUser(null);throw new Error("Session expired. Please sign in again.");}response=await fetch(`${API}${path}`,{...init,headers:{"Content-Type":"application/json",Authorization:`Bearer ${accessToken}`,...init.headers}});}
    const body=await response.json().catch(()=>({}));if(!response.ok)throw new Error(body.message??body.title??"Request failed.");return body;
  },[tokens,refresh]);
  const loadRideHistory=useCallback(async(isActive:()=>boolean)=>{const history=await apiFetch("/api/v1/rides/mine") as RideHistoryItem[];if(!isActive())return;setRideHistory(history);const active=findActiveRide(history, "rider");if(active){const details=await apiFetch("/api/v1/rides/"+encodeURIComponent(active.rideId));if(!isActive())return;setRide(current=>current?.rideId===details.rideId?{...current,...details}:details);}else if(isActive()){setRide(null);}},[apiFetch]);
  useEffect(()=>{let active=true;(async()=>{try{const at=await SecureStore.getItemAsync("turnova.access");const rt=await SecureStore.getItemAsync("turnova.refresh");if(at&&rt){try{const me=await request("/api/v1/auth/me",{},at);if(active){setTokens({accessToken:at,refreshToken:rt});setUser(me);}}catch{try{const result=await request("/api/v1/auth/refresh",{method:"POST",body:JSON.stringify({refreshToken:rt})});if(active)await accept(result);}catch{await clearTokens();}}}}catch{if(active)setError("Unable to restore session.");}finally{if(active)setLoading(false);}})();return()=>{active=false;};},[accept]);
  useEffect(()=>{
    if(!ride||!isActiveRide(ride,"rider")||!tokens)return;
    let active=true;
    const connection=new HubConnectionBuilder()
      .withUrl(`${API}/hubs/rides?rideId=${encodeURIComponent(ride.rideId)}`,{
        accessTokenFactory:async()=>await SecureStore.getItemAsync("turnova.access")??tokens.accessToken,
        transport:HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    const refreshAgreements=async()=>{
      const history=await apiFetch(`/api/v1/rides/${ride.rideId}/agreements`) as AgreementVersion[];
      if(active)setAgreementHistory(history);
    };
    const refreshRiderState=async()=>{
      try{
        await loadRideHistory(()=>active);
        if(active)await refreshAgreements();
      }catch(e){if(active)setError(e instanceof Error?e.message:"Unable to recover the latest ride state.");}
    };
    connection.on("DriverLocationUpdated",(update:{rideId:string;latitude:number;longitude:number;locationUpdatedAt:string})=>{
      if(active)setRide(current=>current?.rideId===update.rideId&&canShareActiveRideLocation(current)?{...current,driverLatitude:update.latitude,driverLongitude:update.longitude,locationUpdatedAt:update.locationUpdatedAt}:current);
    });
    connection.on("RideStatusChanged",(update:{rideId:string;status:string})=>{
      if(active){
        const locationSharingActive=canShareActiveRideLocation({status:update.status,locationSharingActive:true});
        setRide(current=>current?.rideId===update.rideId?{
          ...current,
          status:update.status,
          locationSharingActive,
          ...(locationSharingActive?{}:{driverLatitude:undefined,driverLongitude:undefined,locationUpdatedAt:undefined})
        }:current);
      }
    });
    connection.on("RideAgreementChanged",(update:{rideId:string})=>{
      if(active&&update.rideId===ride.rideId)void refreshAgreements().catch(e=>setError(e instanceof Error?e.message:"Unable to refresh fare agreement."));
    });
    connection.onreconnected(()=>{void refreshRiderState();});
    void connection.start().then(refreshRiderState).catch(()=>{
      if(active)setError("Live ride updates are temporarily unavailable. The current trip state will refresh after reconnection.");
    });
    return()=>{
      active=false;
      connection.off("DriverLocationUpdated");
      connection.off("RideStatusChanged");
      connection.off("RideAgreementChanged");
      void connection.stop();
    };
  },[ride?.rideId,ride?.status,tokens?.accessToken,apiFetch,loadRideHistory]);
  const authenticate=async()=>{setLoading(true);setError("");try{const path=mode==="register"?"/api/v1/auth/rider/register":"/api/v1/auth/rider/login";const result=await request(path,{method:"POST",body:JSON.stringify({email:email.trim(),password,name:name.trim()||null})});await accept(result);}catch(e){setError(e instanceof Error?e.message:"Authentication failed.");}finally{setLoading(false);}};
  const logout=async()=>{try{if(tokens)await request("/api/v1/auth/logout",{method:"POST"},tokens.accessToken);}catch{}await clearTokens();setTokens(null);setUser(null);setRide(null);setRideHistory([]);setDiscovery(null);setSelectedDriverId(null);setAgreementHistory([]);setAgreementAmount("");};
  const findNearbyVehicles=async()=>{setLoading(true);setError("");setSelectedDriverId(null);try{const permission=await Location.requestForegroundPermissionsAsync();if(!permission.granted)throw new Error("Location permission is required to find nearby Cab drivers.");const position=await Location.getCurrentPositionAsync({accuracy:Location.Accuracy.Balanced});const coordinates={latitude:position.coords.latitude,longitude:position.coords.longitude};setPickupCoordinates(coordinates);const query="latitude="+encodeURIComponent(String(coordinates.latitude))+"&longitude="+encodeURIComponent(String(coordinates.longitude))+"&passengers="+encodeURIComponent(String(Number(passengers)||1));const result=await apiFetch("/api/v1/rider/discovery?"+query);setDiscovery(result as DiscoveryResult);}catch(e){setDiscovery(null);setError(e instanceof Error?e.message:"Unable to discover nearby Cab drivers.");}finally{setLoading(false);}};
  const cancelRide=async()=>{if(!ride)return;setLoading(true);setError("");try{const updated=await apiFetch(`/api/v1/rides/${ride.rideId}/cancel`,{method:"POST"});setRide(updated);}catch(e){setError(e instanceof Error?e.message:"Unable to cancel ride.");}finally{setLoading(false);}};
   const requestSelectedDriver=async()=>{if(!selectedDriver||!selectedUnion)return;setLoading(true);setError("");try{let coordinates=pickupCoordinates;if(!coordinates){const permission=await Location.requestForegroundPermissionsAsync();if(!permission.granted)throw new Error("Location permission is required to request the selected driver.");const position=await Location.getCurrentPositionAsync({accuracy:Location.Accuracy.Balanced});coordinates={latitude:position.coords.latitude,longitude:position.coords.longitude};setPickupCoordinates(coordinates);}const stops=stopsText.split(/\r?\n/).map(stop=>stop.trim()).filter(Boolean);const created=await apiFetch("/api/v1/rider/bookings",{method:"POST",headers:{"Idempotency-Key":bookingRequestKey},body:JSON.stringify({selectedDriverId:selectedDriver.driverId,unionId:selectedUnion.unionId,pickup,dropoff,stops,passengers:Number(passengers)||1,pickupLatitude:coordinates.latitude,pickupLongitude:coordinates.longitude})});setRide(created as Ride);setRideHistory(current=>[created as RideHistoryItem,...current.filter(item=>item.rideId!==created.rideId)]);setAgreementHistory([]);setAgreementAmount("");setTab("Activity");setDiscovery(null);setSelectedDriverId(null);setBookingRequestKey(`${Date.now()}-${Math.random().toString(36).slice(2)}`);}catch(e){setError(e instanceof Error?e.message:"Unable to request the selected driver.");}finally{setLoading(false);}};
  const loadAgreementHistory=useCallback(async(rideId:string)=>{const result=await apiFetch(`/api/v1/rides/${rideId}/agreements`) as AgreementVersion[];setAgreementHistory(result);},[apiFetch]);
  useEffect(()=>{if(!ride){setAgreementHistory([]);setAgreementAmount("");return;}if(tab!=="Activity")return;let active=true;void apiFetch(`/api/v1/rides/${ride.rideId}/agreements`).then(result=>{if(active)setAgreementHistory(result as AgreementVersion[]);}).catch(()=>{if(active)setAgreementHistory([]);});return()=>{active=false;};},[ride?.rideId,tab,apiFetch]);
  const proposeAgreement=async()=>{if(!ride)return;const amount=Number(agreementAmount);if(!Number.isFinite(amount)||amount<=0){setError("Enter a positive agreed amount in INR.");return;}setError("");try{await apiFetch(`/api/v1/rides/${ride.rideId}/agreements`,{method:"POST",headers:{"Idempotency-Key":`rider-${Date.now()}-${Math.random().toString(36).slice(2)}`},body:JSON.stringify({amount,currency:"INR"})});setAgreementAmount("");await loadAgreementHistory(ride.rideId);}catch(e){setError(e instanceof Error?e.message:"Unable to submit fare proposal.");}};
  const confirmAgreement=async()=>{if(!ride)return;const current=agreementHistory[agreementHistory.length-1];if(!current)return;setError("");try{await apiFetch(`/api/v1/rides/${ride.rideId}/agreements/${current.version}/confirm`,{method:"POST"});await loadAgreementHistory(ride.rideId);}catch(e){setError(e instanceof Error?e.message:"Unable to confirm fare agreement.");}};

  useEffect(()=>{if(!user||tab!=="Activity")return;let active=true;void loadRideHistory(()=>active).catch(e=>{if(active)setError(e instanceof Error?e.message:"Unable to load trip history.");});return()=>{active=false;};},[user?.id,tab,loadRideHistory,ride?.status]);
  const resolvedTheme = themePreference === "system" ? (systemColorScheme ?? "light") : themePreference;
  const colors = resolvedTheme === "dark" ? darkColors : lightColors;
  const styles = createStyles(colors);
  const selectedDriver = discovery?.unions.flatMap(group=>group.drivers).find(driver=>driver.driverId===selectedDriverId) ?? null;
  const selectedUnion = discovery?.unions.find(group=>group.drivers.some(driver=>driver.driverId===selectedDriverId)) ?? null;
  const themePicker = <View style={styles.themePicker}>
    {(["system", "light", "dark"] as const).map(option => <Pressable key={option} onPress={() => selectTheme(option)} style={[styles.themeOption, themePreference === option && styles.themeOptionSelected]}><Text style={[styles.themeOptionText, themePreference === option && styles.themeOptionTextSelected]}>{option === "system" ? "System" : option === "light" ? "Light" : "Dark"}</Text></Pressable>)}
  </View>;
  if(loading&&!user)return <SafeAreaView style={styles.safe}><ActivityIndicator color={colors.accent} style={{marginTop:80}}/></SafeAreaView>;
  if(!user)return <SafeAreaView style={styles.safe}><StatusBar style={resolvedTheme === "dark" ? "light" : "dark"}/><View style={styles.header}><TurnOvaLogo subtitle="Everyone’s Turn Matters." /></View><ScrollView contentContainerStyle={styles.content}><Text style={styles.eyebrow}>RIDER ACCOUNT</Text><Text style={styles.title}>{mode==="login"?"Welcome back":"Create your account"}</Text><Text style={styles.sub}>Sign in securely with your email and password.</Text>{themePicker}<View style={styles.card}>{mode==="register"&&<TextInput value={name} onChangeText={setName} style={styles.input} placeholderTextColor={colors.muted} placeholder="Full name"/>}<TextInput value={email} onChangeText={setEmail} style={styles.input} placeholderTextColor={colors.muted} placeholder="Email address" keyboardType="email-address" autoCapitalize="none" autoCorrect={false}/><TextInput value={password} onChangeText={setPassword} style={styles.input} placeholderTextColor={colors.muted} placeholder="Password" secureTextEntry/><Pressable style={styles.primary} disabled={loading||!email.trim()||!password} onPress={()=>void authenticate()}><Text style={styles.primaryText}>{loading?"Please wait…":mode==="login"?"Sign in":"Create rider account"}</Text></Pressable><Pressable onPress={()=>{setMode(mode==="login"?"register":"login");setError("");}}><Text style={styles.link}>{mode==="login"?"New to TurnOva? Create an account":"Already registered? Sign in"}</Text></Pressable>{!!error&&<Text style={styles.error}>{error}</Text>}</View></ScrollView></SafeAreaView>;
  const tabs=["Book","Activity","Offers","Profile"] as const;
  return <SafeAreaView style={styles.safe}><StatusBar style={resolvedTheme === "dark" ? "light" : "dark"}/><View style={styles.header}><TurnOvaLogo subtitle={`Rider · ${user.name||user.email}`} compact /></View><ScrollView contentContainerStyle={styles.content}>
    {tab==="Book"&&<><Text style={styles.eyebrow}>YOUR CITY, YOUR RIDE</Text><Text style={styles.title}>Where to?</Text><Text style={styles.sub}>Discover Cab drivers from approved nearby unions, then choose who to contact.</Text><View style={styles.card}><Text style={styles.label}>PICKUP NOTE / LANDMARK</Text><TextInput value={pickup} onChangeText={value=>{setPickup(value);setDiscovery(null);setSelectedDriverId(null);setBookingRequestKey(`${Date.now()}-${Math.random().toString(36).slice(2)}`);}} style={styles.input} placeholderTextColor={colors.muted} placeholder="Pickup"/><Text style={styles.label}>DROP-OFF NOTE / LANDMARK</Text><TextInput value={dropoff} onChangeText={value=>{setDropoff(value);setDiscovery(null);setSelectedDriverId(null);setBookingRequestKey(`${Date.now()}-${Math.random().toString(36).slice(2)}`);}} style={styles.input} placeholderTextColor={colors.muted} placeholder="Drop-off"/><Text style={styles.label}>OPTIONAL INTERMEDIATE STOPS</Text><TextInput value={stopsText} onChangeText={value=>{setStopsText(value);setDiscovery(null);setSelectedDriverId(null);setBookingRequestKey(`${Date.now()}-${Math.random().toString(36).slice(2)}`);}} multiline style={[styles.input,{minHeight:72,textAlignVertical:"top"}]} placeholderTextColor={colors.muted} placeholder="Add one stop per line"/><Text style={styles.label}>VEHICLE CATEGORY</Text><View style={{paddingVertical:10,paddingHorizontal:14,borderRadius:10,borderWidth:1,borderColor:colors.accent,backgroundColor:colors.surface}}><Text style={{color:colors.text,fontWeight:"700"}}>Cab</Text><Text style={styles.muted}>TurnOva currently supports Cab rides only.</Text></View><Text style={styles.label}>PASSENGERS (1–6)</Text><TextInput value={passengers} onChangeText={value=>{setPassengers(String(Math.min(6,Math.max(1,Number(value)||1))));setDiscovery(null);setSelectedDriverId(null);setBookingRequestKey(`${Date.now()}-${Math.random().toString(36).slice(2)}`);}} keyboardType="number-pad" style={styles.input} placeholderTextColor={colors.muted}/><Text style={styles.muted}>Your device GPS is used for pickup-zone matching and driver-to-pickup proximity. The text above is a landmark or driver instruction.</Text><Pressable style={styles.secondary} onPress={()=>void findNearbyVehicles()} disabled={loading||!pickup.trim()||!dropoff.trim()}><Text style={styles.secondaryText}>{loading?"Searching…":"Find nearby Cab drivers"}</Text></Pressable>{discovery!==null&&<View style={{marginTop:14}}><Text style={styles.cardTitle}>{discovery.unions.length?discovery.unions.length+" approved union(s) with eligible Cab drivers":"No eligible Cab drivers are currently available in your pickup area."}</Text><Text style={styles.muted}>Journey distance: {discovery.journeyDistanceKm===null?"Unavailable until a routing provider is configured":discovery.journeyDistanceKm.toFixed(1)+" km"}. Driver-to-pickup distances below are separate.</Text>{discovery.unions.map(union=><View key={union.unionId} style={{marginTop:14,padding:12,borderWidth:1,borderColor:colors.border,borderRadius:12}}><Text style={styles.cardTitle}>{union.unionName}</Text>{union.drivers.map(driver=>{const selected=selectedDriverId===driver.driverId;return <Pressable key={driver.driverId} onPress={()=>{setSelectedDriverId(driver.driverId);setBookingRequestKey(`${Date.now()}-${Math.random().toString(36).slice(2)}`);}} style={{flexDirection:"row",alignItems:"center",paddingVertical:10,borderBottomWidth:1,borderBottomColor:colors.border,backgroundColor:selected?colors.subtle:"transparent"}}>{driver.profilePhotoBase64?<Image source={{uri:"data:"+(driver.profilePhotoContentType??"image/jpeg")+";base64,"+driver.profilePhotoBase64}} style={{width:52,height:52,borderRadius:26,marginRight:12}}/>:<View style={{width:52,height:52,borderRadius:26,marginRight:12,backgroundColor:colors.border,alignItems:"center",justifyContent:"center"}}><Text style={styles.muted}>No photo</Text></View>}<View style={{flex:1}}><Text style={styles.cardTitle}>{driver.fullName}</Text><Text style={styles.muted}>{driver.vehicleMake} {driver.vehicleModel} · Cab · up to {driver.passengerCapacity} passenger(s)</Text><Text style={styles.muted}>{driver.distanceToPickupKm.toFixed(1)} km from pickup</Text></View><Text style={{color:colors.accent,fontWeight:"700"}}>{selected?"Selected":"Select"}</Text></Pressable>;})}</View>)}</View>}{selectedDriverId!==null&&selectedDriver!==null&&<View style={{marginTop:14,padding:12,borderRadius:10,backgroundColor:colors.surface,borderWidth:1,borderColor:colors.accent}}><Text style={styles.cardTitle}>Selected · {selectedDriver.fullName}</Text><Text style={styles.muted}>{selectedDriver.vehicleMake} {selectedDriver.vehicleModel} · Cab · {selectedDriver.distanceToPickupKm.toFixed(1)} km from pickup</Text><Text style={styles.label}>TRIP DETAILS</Text><Text style={styles.muted}>From: {pickup}</Text><Text style={styles.muted}>To: {dropoff}</Text>{stopsText.trim().length>0&&<Text style={styles.muted}>Stops: {stopsText.split(/\r?\n/).map(stop=>stop.trim()).filter(Boolean).join(" → ")}</Text>}<Text style={styles.muted}>Journey distance is unavailable until routing is configured. The selected Driver will receive this request for direct fare negotiation. A server-approved negotiation deadline is required; booking remains unavailable until that policy is configured. Fare is negotiated directly; TurnOva does not quote or collect payment.</Text><Pressable style={styles.primary} disabled={loading} onPress={()=>void requestSelectedDriver()}><Text style={styles.primaryText}>{loading?"Requesting…":"Request this driver"}</Text></Pressable><View style={{flexDirection:"row",gap:8,marginTop:10}}><Pressable style={styles.secondary} disabled><Text style={styles.secondaryText}>Call · provider pending</Text></Pressable><Pressable style={styles.secondary} disabled><Text style={styles.secondaryText}>Chat · provider pending</Text></Pressable></View></View>}</View></>}
    {tab==="Activity"&&<><Text style={styles.title}>Activity</Text>{ride&&<View style={styles.card}><Text style={styles.status}>{ride.status.toUpperCase()}</Text><Text style={styles.cardTitle}>{ride.driverDisplayName ? "Selected driver request" : "Ride requested"}</Text><Text style={styles.muted}>Reference · {ride.rideId}</Text>{ride.driverDisplayName&&<><Text style={styles.label}>SELECTED DRIVER</Text><Text style={styles.cardTitle}>{ride.driverDisplayName}</Text><Text style={styles.muted}>{ride.driverVehicleMake} {ride.driverVehicleModel} · Cab{ride.driverDistanceToPickupKm!=null?" · "+ride.driverDistanceToPickupKm.toFixed(1)+" km from pickup":""}</Text>{ride.unionName&&<Text style={styles.muted}>Union · {ride.unionName}</Text>}</>}<Text style={styles.label}>PICKUP</Text><Text style={styles.cardTitle}>{ride.pickup}</Text><Text style={styles.label}>DROP-OFF</Text><Text style={styles.cardTitle}>{ride.dropoff}</Text>{ride.stops?.length? <><Text style={styles.label}>STOPS</Text><Text style={styles.cardTitle}>{ride.stops.join(" → ")}</Text></>:null}<Text style={styles.muted}>{ride.serviceClass.toUpperCase()} · {ride.passengers} passenger(s) · Fare is negotiated directly with the driver; TurnOva does not quote or collect payment.</Text><View style={{marginTop:14,paddingTop:12,borderTopWidth:1,borderTopColor:colors.border}}><Text style={styles.cardTitle}>Direct fare negotiation · INR</Text>{agreementHistory.map(item=><View key={item.version} style={{paddingVertical:7}}><Text style={styles.muted}>Version {item.version} · ₹{item.amount.toFixed(2)} · proposed by {item.proposedBy}</Text><Text style={styles.muted}>Rider: {item.riderConfirmed?"Confirmed":"Pending"} · Driver: {item.driverConfirmed?"Confirmed":"Pending"}</Text></View>)}{["Negotiating","AgreementConfirmed","Accepted","Arrived","InProgress"].includes(ride.status)&&<><TextInput value={agreementAmount} onChangeText={setAgreementAmount} keyboardType="decimal-pad" style={styles.input} placeholderTextColor={colors.muted} placeholder="Propose / counter amount (₹)"/><Pressable style={styles.secondary} disabled={loading||!agreementAmount.trim()} onPress={()=>void proposeAgreement()}><Text style={styles.secondaryText}>Propose / counter fare</Text></Pressable>{agreementHistory.length>0&&<Pressable style={styles.secondary} disabled={loading||agreementHistory[agreementHistory.length-1].riderConfirmed} onPress={()=>void confirmAgreement()}><Text style={styles.secondaryText}>{agreementHistory[agreementHistory.length-1].riderConfirmed?"You confirmed this version":"Confirm current agreement"}</Text></Pressable>}</>}</View>{ride.driverLatitude!=null&&ride.driverLongitude!=null&&<MapView customMapStyle={resolvedTheme === "dark" ? darkMapStyle : []} style={{height:260,borderRadius:14,marginTop:16}} initialRegion={{latitude:ride.driverLatitude,longitude:ride.driverLongitude,latitudeDelta:0.025,longitudeDelta:0.025}}><Marker coordinate={{latitude:ride.driverLatitude,longitude:ride.driverLongitude}} title="Your driver"/>{ride.pickupLatitude!=null&&ride.pickupLongitude!=null&&<Marker coordinate={{latitude:ride.pickupLatitude,longitude:ride.pickupLongitude}} title="Pickup" pinColor={colors.accent}/>}</MapView>}<Text style={styles.muted}>{ride.status==="Negotiating"?"Your selected driver is negotiating the fare with you. "+(ride.negotiationExpiresAtUtc?"Negotiation expires at "+new Date(ride.negotiationExpiresAtUtc).toLocaleTimeString()+".":"A configured negotiation deadline applies."):ride.status==="AgreementConfirmed"?"Both parties confirmed the current fare. Waiting for the Driver to accept the trip.":ride.status==="Accepted"?"Driver accepted. Live location updates use SignalR.":ride.status==="Arrived"?"Your driver has arrived.":ride.status==="InProgress"?"Your ride is in progress.":ride.status==="Completed"?"Ride completed.":ride.status==="Expired"?"The selected-driver negotiation expired. Choose another available Driver.":ride.status==="Cancelled"?"Ride cancelled.":"Waiting for a driver to accept your request."}</Text>{["Requested","Negotiating","AgreementConfirmed","Accepted","Arrived"].includes(ride.status)&&<Pressable style={styles.secondary} disabled={loading} onPress={()=>void cancelRide()}><Text style={styles.secondaryText}>Cancel ride</Text></Pressable>}</View>}{rideHistory.filter(item=>item.rideId!==ride?.rideId).length>0&&<View style={styles.card}><Text style={styles.cardTitle}>Trip history</Text>{rideHistory.filter(item=>item.rideId!==ride?.rideId).map(item=><View key={item.rideId} style={{paddingVertical:12,borderBottomWidth:1,borderBottomColor:colors.border}}><Text style={styles.status}>{item.status.toUpperCase()}</Text><Text style={styles.cardTitle}>{item.pickup} → {item.dropoff}</Text><Text style={styles.muted}>{item.serviceClass.toUpperCase()} · {item.passengers} passenger(s)</Text><Text style={styles.muted}>{new Date(item.requestedAt).toLocaleString()}</Text><Text style={styles.muted}>Reference · {item.rideId}</Text></View>)}</View>}{!ride&&rideHistory.length===0&&<View style={styles.card}><Text style={styles.muted}>No rides yet. Discover available Cab drivers to get started.</Text></View>}</>}
    {tab==="Offers"&&<><Text style={styles.title}>Offers</Text><View style={styles.card}><Text style={styles.muted}>Fare proposals and confirmation history are shown in Activity for your assigned trip. No platform fare is quoted or collected.</Text></View></>}
    {tab==="Profile"&&<><Text style={styles.title}>Your profile</Text>{themePicker}<Text style={styles.label}>APP THEME</Text><View style={styles.card}><Text style={styles.cardTitle}>{user.name||"Rider"}</Text><Text style={styles.muted}>{user.email}</Text><Text style={styles.muted}>Account role: {user.role}</Text><Pressable style={styles.secondary} onPress={()=>void logout()}><Text style={styles.secondaryText}>Sign out</Text></Pressable></View></>}
    {!!error&&<Text style={styles.error}>{error}</Text>}
  </ScrollView><View style={styles.tabBar}>{tabs.map(item=><Pressable key={item} style={styles.tab} onPress={()=>setTab(item)}><Text style={[styles.tabText,tab===item&&styles.tabActive]}>{item}</Text></Pressable>)}</View></SafeAreaView>;
}
function createStyles(colors: ThemeColors) {
  return StyleSheet.create({safe:{flex:1,backgroundColor:colors.background},header:{backgroundColor:colors.header,padding:22,paddingTop:18},brand:{color:"#FFFFFF",fontSize:25,fontWeight:"800"},tagline:{color:colors.subtle,marginTop:4,fontSize:12},content:{padding:22,paddingBottom:34},eyebrow:{color:colors.tabActive,fontWeight:"800",fontSize:11,letterSpacing:1.3,marginTop:8},title:{color:colors.text,fontSize:28,fontWeight:"800",marginTop:8},sub:{color:colors.muted,fontSize:14,marginTop:6,marginBottom:20},card:{backgroundColor:colors.surface,padding:20,borderRadius:18,marginBottom:15},input:{borderWidth:1,borderColor:colors.border,borderRadius:12,padding:14,color:colors.text,marginBottom:12},label:{color:colors.label,fontSize:10,fontWeight:"800",letterSpacing:1.1,marginTop:10},primary:{backgroundColor:colors.accent,borderRadius:12,padding:15,alignItems:"center",marginTop:12},primaryText:{color:colors.accentText,fontWeight:"800"},link:{color:colors.tabActive,fontWeight:"800",textAlign:"center",marginTop:18},muted:{color:colors.muted,fontSize:13,marginTop:7,lineHeight:19},cardTitle:{color:colors.text,fontSize:17,fontWeight:"800",marginTop:8},status:{color:colors.tabActive,fontWeight:"900"},error:{color:colors.danger,marginTop:12},secondary:{borderWidth:1,borderColor:colors.border,borderRadius:12,padding:13,alignItems:"center",marginTop:20},secondaryText:{color:colors.text,fontWeight:"800"},tabBar:{flexDirection:"row",backgroundColor:colors.surface,paddingVertical:14},tab:{flex:1,alignItems:"center"},tabText:{color:colors.tabInactive,fontSize:12,fontWeight:"700"},tabActive:{color:colors.tabActive},themePicker:{flexDirection:"row",gap:8,marginTop:10,marginBottom:10},themeOption:{flex:1,paddingVertical:10,borderRadius:10,borderWidth:1,borderColor:colors.border,alignItems:"center",backgroundColor:colors.surface},themeOptionSelected:{backgroundColor:colors.header,borderColor:colors.header},themeOptionText:{color:colors.muted,fontWeight:"700",fontSize:12},themeOptionTextSelected:{color:"#FFFFFF",fontWeight:"800"}});
}
