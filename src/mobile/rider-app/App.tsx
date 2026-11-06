import { StatusBar } from "expo-status-bar";
import * as SecureStore from "expo-secure-store";
import { useCallback, useEffect, useRef, useState } from "react";
import { useColorScheme } from "react-native";
import { ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import TurnOvaLogo from "./TurnOvaLogo";
import { singleFlight } from "../shared/refreshCoordinator";
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
type ServiceClass = "scooty" | "bike" | "auto" | "cab";
type Ride = { rideId: string; pickup: string; dropoff: string; passengers: number; serviceClass: ServiceClass; status: string; estimatedFareInr: number; pickupLatitude?: number; pickupLongitude?: number; driverLatitude?: number; driverLongitude?: number; locationUpdatedAt?: string };
type NearbyVehicle = { vehicleMake: string; vehicleModel: string; serviceClass: ServiceClass; passengerCapacity: number; distanceKm: number; locationUpdatedAtUtc: string };
async function saveTokens(t: Tokens) { await SecureStore.setItemAsync("turnova.access", t.accessToken); await SecureStore.setItemAsync("turnova.refresh", t.refreshToken); }
async function clearTokens() { await SecureStore.deleteItemAsync("turnova.access"); await SecureStore.deleteItemAsync("turnova.refresh"); }
async function request(path: string, init: RequestInit = {}, token?: string) {
  const response = await fetch(`${API}${path}`, { ...init, headers: { "Content-Type":"application/json", ...(token ? { Authorization:`Bearer ${token}` } : {}), ...init.headers } });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.message ?? body.title ?? "Request failed.");
  return body;
}

export default function App() {
  const [user,setUser]=useState<User|null>(null);
  const [themePreference, setThemePreference] = useState<ThemePreference>("system");
  const systemColorScheme = useColorScheme(); const [tokens,setTokens]=useState<Tokens|null>(null); const refreshInFlight=useRef<Promise<string>|null>(null);
  const [tab,setTab]=useState<"Book"|"Activity"|"Offers"|"Profile">("Book");
  const [mode,setMode]=useState<"login"|"register">("login"); const [email,setEmail]=useState(""); const [password,setPassword]=useState(""); const [name,setName]=useState("");
  const [pickup,setPickup]=useState("KPHB, Hyderabad"); const [dropoff,setDropoff]=useState("HITEC City, Hyderabad"); const [passengers,setPassengers]=useState("1");
  const [serviceClass,setServiceClass]=useState<ServiceClass>("auto");
  const [ride,setRide]=useState<Ride|null>(null); const [loading,setLoading]=useState(true); const [error,setError]=useState("");
  const [nearbyVehicles,setNearbyVehicles]=useState<NearbyVehicle[]|null>(null);
  const [pickupCoordinates,setPickupCoordinates]=useState<{latitude:number;longitude:number}|null>(null);
  const [requestKey,setRequestKey]=useState(()=>`${Date.now()}-${Math.random().toString(36).slice(2)}`);
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
  useEffect(()=>{let active=true;(async()=>{try{const at=await SecureStore.getItemAsync("turnova.access");const rt=await SecureStore.getItemAsync("turnova.refresh");if(at&&rt){try{const me=await request("/api/v1/auth/me",{},at);if(active){setTokens({accessToken:at,refreshToken:rt});setUser(me);}}catch{try{const result=await request("/api/v1/auth/refresh",{method:"POST",body:JSON.stringify({refreshToken:rt})});if(active)await accept(result);}catch{await clearTokens();}}}}catch{if(active)setError("Unable to restore session.");}finally{if(active)setLoading(false);}})();return()=>{active=false;};},[accept]);
  useEffect(()=>{
    if(!ride||tab!=="Activity"||!tokens)return;
    const connection=new HubConnectionBuilder()
      .withUrl(`${API}/hubs/rides?rideId=${encodeURIComponent(ride.rideId)}`,{
        accessTokenFactory:async()=>await SecureStore.getItemAsync("turnova.access")??tokens.accessToken,
        transport:HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on("DriverLocationUpdated",(update:{rideId:string;latitude:number;longitude:number;locationUpdatedAt:string})=>{
      setRide(current=>current?.rideId===update.rideId?{...current,driverLatitude:update.latitude,driverLongitude:update.longitude,locationUpdatedAt:update.locationUpdatedAt}:current);
    });
    connection.on("RideStatusChanged",(update:{rideId:string;status:string})=>{
      setRide(current=>current?.rideId===update.rideId?{...current,status:update.status}:current);
    });
    void connection.start().catch(()=>{});
    return()=>{void connection.stop();};
  },[ride?.rideId,tab,tokens?.accessToken]);
  const authenticate=async()=>{setLoading(true);setError("");try{const path=mode==="register"?"/api/v1/auth/rider/register":"/api/v1/auth/rider/login";const result=await request(path,{method:"POST",body:JSON.stringify({email:email.trim(),password,name:name.trim()||null})});await accept(result);}catch(e){setError(e instanceof Error?e.message:"Authentication failed.");}finally{setLoading(false);}};
  const logout=async()=>{try{if(tokens)await request("/api/v1/auth/logout",{method:"POST"},tokens.accessToken);}catch{}await clearTokens();setTokens(null);setUser(null);setRide(null);};
  const findNearbyVehicles=async()=>{setLoading(true);setError("");try{const permission=await Location.requestForegroundPermissionsAsync();if(!permission.granted)throw new Error("Location permission is required to find nearby vehicles.");const position=await Location.getCurrentPositionAsync({accuracy:Location.Accuracy.Balanced});const coordinates={latitude:position.coords.latitude,longitude:position.coords.longitude};setPickupCoordinates(coordinates);const query=new URLSearchParams({latitude:String(coordinates.latitude),longitude:String(coordinates.longitude),serviceClass,passengers:String(Number(passengers)||1)});const vehicles=await apiFetch("/api/v1/rides/nearby?"+query.toString());setNearbyVehicles(vehicles as NearbyVehicle[]);}catch(e){setNearbyVehicles(null);setError(e instanceof Error?e.message:"Unable to find nearby vehicles.");}finally{setLoading(false);}};
  const book=async()=>{setLoading(true);setError("");try{let coordinates=pickupCoordinates;if(!coordinates){const permission=await Location.requestForegroundPermissionsAsync();if(!permission.granted)throw new Error("Location permission is required to set your pickup point.");const position=await Location.getCurrentPositionAsync({accuracy:Location.Accuracy.Balanced});coordinates={latitude:position.coords.latitude,longitude:position.coords.longitude};setPickupCoordinates(coordinates);}const result=await apiFetch("/api/v1/rides/request",{method:"POST",headers:{"Idempotency-Key":requestKey},body:JSON.stringify({pickup,dropoff,passengers:Number(passengers)||1,serviceClass,pickupLatitude:coordinates.latitude,pickupLongitude:coordinates.longitude})});setRide(result);setRequestKey(Date.now()+"-"+Math.random().toString(36).slice(2));setTab("Activity");}catch(e){setError(e instanceof Error?e.message:"Unable to request ride.");}finally{setLoading(false);}};
  const cancelRide=async()=>{if(!ride)return;setLoading(true);setError("");try{const updated=await apiFetch(`/api/v1/rides/${ride.rideId}/cancel`,{method:"POST"});setRide(updated);}catch(e){setError(e instanceof Error?e.message:"Unable to cancel ride.");}finally{setLoading(false);}};
  const resolvedTheme = themePreference === "system" ? (systemColorScheme ?? "light") : themePreference;
  const colors = resolvedTheme === "dark" ? darkColors : lightColors;
  const styles = createStyles(colors);
  const themePicker = <View style={styles.themePicker}>
    {(["system", "light", "dark"] as const).map(option => <Pressable key={option} onPress={() => selectTheme(option)} style={[styles.themeOption, themePreference === option && styles.themeOptionSelected]}><Text style={[styles.themeOptionText, themePreference === option && styles.themeOptionTextSelected]}>{option === "system" ? "System" : option === "light" ? "Light" : "Dark"}</Text></Pressable>)}
  </View>;
  if(loading&&!user)return <SafeAreaView style={styles.safe}><ActivityIndicator color={colors.accent} style={{marginTop:80}}/></SafeAreaView>;
  if(!user)return <SafeAreaView style={styles.safe}><StatusBar style={resolvedTheme === "dark" ? "light" : "dark"}/><View style={styles.header}><TurnOvaLogo subtitle="Everyone’s Turn Matters." /></View><ScrollView contentContainerStyle={styles.content}><Text style={styles.eyebrow}>RIDER ACCOUNT</Text><Text style={styles.title}>{mode==="login"?"Welcome back":"Create your account"}</Text><Text style={styles.sub}>Sign in securely with your email and password.</Text>{themePicker}<View style={styles.card}>{mode==="register"&&<TextInput value={name} onChangeText={setName} style={styles.input} placeholderTextColor={colors.muted} placeholder="Full name"/>}<TextInput value={email} onChangeText={setEmail} style={styles.input} placeholderTextColor={colors.muted} placeholder="Email address" keyboardType="email-address" autoCapitalize="none" autoCorrect={false}/><TextInput value={password} onChangeText={setPassword} style={styles.input} placeholderTextColor={colors.muted} placeholder="Password" secureTextEntry/><Pressable style={styles.primary} disabled={loading||!email.trim()||!password} onPress={()=>void authenticate()}><Text style={styles.primaryText}>{loading?"Please wait…":mode==="login"?"Sign in":"Create rider account"}</Text></Pressable><Pressable onPress={()=>{setMode(mode==="login"?"register":"login");setError("");}}><Text style={styles.link}>{mode==="login"?"New to TurnOva? Create an account":"Already registered? Sign in"}</Text></Pressable>{!!error&&<Text style={styles.error}>{error}</Text>}</View></ScrollView></SafeAreaView>;
  const tabs=["Book","Activity","Offers","Profile"] as const;
  return <SafeAreaView style={styles.safe}><StatusBar style={resolvedTheme === "dark" ? "light" : "dark"}/><View style={styles.header}><TurnOvaLogo subtitle={`Rider · ${user.name||user.email}`} compact /></View><ScrollView contentContainerStyle={styles.content}>
    {tab==="Book"&&<><Text style={styles.eyebrow}>YOUR CITY, YOUR RIDE</Text><Text style={styles.title}>Where to?</Text><Text style={styles.sub}>A fair ride starts with a simple booking.</Text><View style={styles.card}><Text style={styles.label}>PICKUP LOCATION</Text><TextInput value={pickup} onChangeText={value=>{setPickup(value);setNearbyVehicles(null);}} style={styles.input} placeholderTextColor={colors.muted} placeholder="Pickup"/><Text style={styles.label}>DROP-OFF LOCATION</Text><TextInput value={dropoff} onChangeText={setDropoff} style={styles.input} placeholderTextColor={colors.muted} placeholder="Drop-off"/><Text style={styles.label}>VEHICLE TYPE</Text><View style={{flexDirection:"row",flexWrap:"wrap",gap:8,marginBottom:14}}>{([{value:"scooty",label:"Scooty"},{value:"bike",label:"Bike"},{value:"auto",label:"Auto"},{value:"cab",label:"Cab"}] as const).map(option=><Pressable key={option.value} onPress={()=>{setServiceClass(option.value);setNearbyVehicles(null);}} style={{paddingVertical:10,paddingHorizontal:14,borderRadius:10,borderWidth:1,borderColor:serviceClass===option.value?colors.accent:colors.border,backgroundColor:serviceClass===option.value?colors.accent:colors.surface}}><Text style={{color:serviceClass===option.value?colors.accentText:colors.text,fontWeight:"700"}}>{option.label}</Text></Pressable>)}</View><Text style={styles.label}>PASSENGERS (1–6)</Text><TextInput value={passengers} onChangeText={v=>{setPassengers(String(Math.min(6,Math.max(1,Number(v)||1))));setNearbyVehicles(null);}} keyboardType="number-pad" style={styles.input} placeholderTextColor={colors.muted}/><Text style={styles.muted}>Your current GPS location is used to determine the pickup zone and nearby vehicle distances.</Text><Pressable style={styles.secondary} onPress={()=>void findNearbyVehicles()} disabled={loading}><Text style={styles.secondaryText}>{loading?"Searching…":"Find nearby vehicles"}</Text></Pressable>{nearbyVehicles!==null&&<View style={{marginTop:14}}><Text style={styles.cardTitle}>{nearbyVehicles.length?nearbyVehicles.length+" matching vehicle(s) nearby":"No matching vehicles are currently online in your pickup zone."}</Text>{nearbyVehicles.map((vehicle,index)=><View key={vehicle.vehicleMake+"-"+vehicle.vehicleModel+"-"+index} style={{paddingVertical:10,borderBottomWidth:1,borderBottomColor:colors.border}}><Text style={styles.cardTitle}>{vehicle.vehicleMake} {vehicle.vehicleModel} · {vehicle.serviceClass.toUpperCase()}</Text><Text style={styles.muted}>Capacity up to {vehicle.passengerCapacity} passenger(s) · {vehicle.distanceKm.toFixed(1)} km away</Text></View>)}</View>}<Pressable style={styles.primary} onPress={()=>void book()} disabled={loading||!pickup.trim()||!dropoff.trim()}><Text style={styles.primaryText}>{loading?"Please wait…":"Confirm ride"}</Text></Pressable></View></>}
    {tab==="Activity"&&<><Text style={styles.title}>Activity</Text>{ride?<View style={styles.card}><Text style={styles.status}>{ride.status.toUpperCase()}</Text><Text style={styles.cardTitle}>Ride requested</Text><Text style={styles.muted}>Reference · {ride.rideId}</Text><Text style={styles.label}>PICKUP</Text><Text style={styles.cardTitle}>{ride.pickup}</Text><Text style={styles.label}>DROP-OFF</Text><Text style={styles.cardTitle}>{ride.dropoff}</Text><Text style={styles.muted}>{ride.serviceClass.toUpperCase()} · {ride.passengers} passenger(s) · Demo estimate ₹{ride.estimatedFareInr}</Text>{ride.driverLatitude!=null&&ride.driverLongitude!=null&&<MapView customMapStyle={resolvedTheme === "dark" ? darkMapStyle : []} style={{height:260,borderRadius:14,marginTop:16}} initialRegion={{latitude:ride.driverLatitude,longitude:ride.driverLongitude,latitudeDelta:0.025,longitudeDelta:0.025}}><Marker coordinate={{latitude:ride.driverLatitude,longitude:ride.driverLongitude}} title="Your driver"/>{ride.pickupLatitude!=null&&ride.pickupLongitude!=null&&<Marker coordinate={{latitude:ride.pickupLatitude,longitude:ride.pickupLongitude}} title="Pickup" pinColor={colors.accent}/>}</MapView>}<Text style={styles.muted}>{ride.status==="Accepted"?"Driver accepted. Live location updates use SignalR.":ride.status==="Arrived"?"Your driver has arrived.":ride.status==="InProgress"?"Your ride is in progress.":ride.status==="Completed"?"Ride completed.":ride.status==="Cancelled"?"Ride cancelled.":"Waiting for a driver to accept your request."}</Text>{["Requested","Accepted","Arrived"].includes(ride.status)&&<Pressable style={styles.secondary} disabled={loading} onPress={()=>void cancelRide()}><Text style={styles.secondaryText}>Cancel ride</Text></Pressable>}</View>:<View style={styles.card}><Text style={styles.muted}>No rides yet. Book your first ride.</Text></View>}</>}
    {tab==="Offers"&&<><Text style={styles.title}>Offers</Text><View style={styles.card}><Text style={styles.muted}>Vehicle options and confirmed fares will appear here when enabled.</Text></View></>}
    {tab==="Profile"&&<><Text style={styles.title}>Your profile</Text>{themePicker}<Text style={styles.label}>APP THEME</Text><View style={styles.card}><Text style={styles.cardTitle}>{user.name||"Rider"}</Text><Text style={styles.muted}>{user.email}</Text><Text style={styles.muted}>Account role: {user.role}</Text><Pressable style={styles.secondary} onPress={()=>void logout()}><Text style={styles.secondaryText}>Sign out</Text></Pressable></View></>}
    {!!error&&<Text style={styles.error}>{error}</Text>}
  </ScrollView><View style={styles.tabBar}>{tabs.map(item=><Pressable key={item} style={styles.tab} onPress={()=>setTab(item)}><Text style={[styles.tabText,tab===item&&styles.tabActive]}>{item}</Text></Pressable>)}</View></SafeAreaView>;
}
function createStyles(colors: ThemeColors) {
  return StyleSheet.create({safe:{flex:1,backgroundColor:colors.background},header:{backgroundColor:colors.header,padding:22,paddingTop:18},brand:{color:"#FFFFFF",fontSize:25,fontWeight:"800"},tagline:{color:colors.subtle,marginTop:4,fontSize:12},content:{padding:22,paddingBottom:34},eyebrow:{color:colors.tabActive,fontWeight:"800",fontSize:11,letterSpacing:1.3,marginTop:8},title:{color:colors.text,fontSize:28,fontWeight:"800",marginTop:8},sub:{color:colors.muted,fontSize:14,marginTop:6,marginBottom:20},card:{backgroundColor:colors.surface,padding:20,borderRadius:18,marginBottom:15},input:{borderWidth:1,borderColor:colors.border,borderRadius:12,padding:14,color:colors.text,marginBottom:12},label:{color:colors.label,fontSize:10,fontWeight:"800",letterSpacing:1.1,marginTop:10},primary:{backgroundColor:colors.accent,borderRadius:12,padding:15,alignItems:"center",marginTop:12},primaryText:{color:colors.accentText,fontWeight:"800"},link:{color:colors.tabActive,fontWeight:"800",textAlign:"center",marginTop:18},muted:{color:colors.muted,fontSize:13,marginTop:7,lineHeight:19},cardTitle:{color:colors.text,fontSize:17,fontWeight:"800",marginTop:8},status:{color:colors.tabActive,fontWeight:"900"},error:{color:colors.danger,marginTop:12},secondary:{borderWidth:1,borderColor:colors.border,borderRadius:12,padding:13,alignItems:"center",marginTop:20},secondaryText:{color:colors.text,fontWeight:"800"},tabBar:{flexDirection:"row",backgroundColor:colors.surface,paddingVertical:14},tab:{flex:1,alignItems:"center"},tabText:{color:colors.tabInactive,fontSize:12,fontWeight:"700"},tabActive:{color:colors.tabActive},themePicker:{flexDirection:"row",gap:8,marginTop:10,marginBottom:10},themeOption:{flex:1,paddingVertical:10,borderRadius:10,borderWidth:1,borderColor:colors.border,alignItems:"center",backgroundColor:colors.surface},themeOptionSelected:{backgroundColor:colors.header,borderColor:colors.header},themeOptionText:{color:colors.muted,fontWeight:"700",fontSize:12},themeOptionTextSelected:{color:"#FFFFFF",fontWeight:"800"}});
}
